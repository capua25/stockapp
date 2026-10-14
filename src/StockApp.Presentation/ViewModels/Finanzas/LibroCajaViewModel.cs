using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Libro caja": un mes (movimientos con saldo corrido, totales por rubro y por fuente) o el año completo
/// (totales por mes y por rubro). Lo que la pantalla muestra y lo que se exporta es el período CARGADO
/// (<see cref="LibroMes"/> o <see cref="LibroAnual"/>), no los filtros: mientras una recarga espera, los filtros pueden
/// ir por delante.
/// </summary>
public partial class LibroCajaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService   _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty] private int _anio;
    [ObservableProperty] private int _mes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModoSeleccionado))]
    private bool _verAnioCompleto;

    [ObservableProperty] private decimal _saldoInicial;
    [ObservableProperty] private decimal _saldoFinal;
    [ObservableProperty] private LibroCajaAnualDto? _libroAnual;

    /// <summary>El mes cargado (null si lo cargado es el año completo o si no hay nada cargado).</summary>
    [ObservableProperty] private LibroCajaMesDto? _libroMes;

    private bool _sinMovimientosEnElPeriodo;
    private int _mesesConMovimientos;

    /// <summary>
    /// True si el período cargado no tiene ningún movimiento: el mes sin filas o el año con los doce meses en cero (el
    /// servidor devuelve siempre los doce, así que un "Count == 0" no sirve). False sin nada cargado: eso es Cargando o
    /// Sin conexión, no un vacío.
    /// </summary>
    public bool SinMovimientosEnElPeriodo
    {
        get => _sinMovimientosEnElPeriodo;
        private set => SetProperty(ref _sinMovimientosEnElPeriodo, value);
    }

    /// <summary>Meses del año cargado con algún ingreso o egreso (pie de la grilla de totales por mes).</summary>
    public int MesesConMovimientos
    {
        get => _mesesConMovimientos;
        private set => SetProperty(ref _mesesConMovimientos, value);
    }

    public ObservableCollection<MovimientoCajaDto> Movimientos { get; } = new();
    public DataGridCollectionView MovimientosView { get; }

    public ObservableCollection<TotalPorClaveDto> TotalesPorRubro { get; } = new();
    public ObservableCollection<TotalPorClaveDto> TotalesPorFuente { get; } = new();

    public LibroCajaViewModel(
        IFinanzasVistasService service, IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion, IPdfExporter pdfExporter, IServicioAperturaArchivo apertura,
        ICurrentSession session, TimeProvider? reloj = null)
    {
        // Año y mes arrancan de la fecha LOCAL: en Uruguay, de 21 a 24 del último día del mes la hora UTC ya es del mes
        // siguiente. Se escriben los campos: no disparan la recarga, la primera carga la pide la vista.
        var hoy = (reloj ?? TimeProvider.System).GetLocalNow();
        _anio = hoy.Year;
        _mes = hoy.Month;

        _service     = service;
        _guardado    = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        MovimientosView = new DataGridCollectionView(Movimientos);
    }

    public const string ModoMes = "Mes";
    public const string ModoAnioCompleto = "Año completo";

    /// <summary>Opciones del selector de período.</summary>
    public static IReadOnlyList<string> OpcionesModo { get; } = [ModoMes, ModoAnioCompleto];

    /// <summary>La opción del selector, traducida a <see cref="VerAnioCompleto"/>.</summary>
    public string ModoSeleccionado
    {
        get => VerAnioCompleto ? ModoAnioCompleto : ModoMes;
        set => VerAnioCompleto = value == ModoAnioCompleto;
    }

    /// <summary>Espera antes de recargar al cambiar año, mes o modo: un año de 4 cifras son 4 cambios y solo el último
    /// tiene que ir al servidor.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista.</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private CancellationTokenSource? _debounceCts;

    partial void OnAnioChanged(int value) => ProgramarRecarga();

    /// <summary>En año completo el mes no cambia la consulta: no recarga.</summary>
    partial void OnMesChanged(int value)
    {
        if (!VerAnioCompleto)
            ProgramarRecarga();
    }

    partial void OnVerAnioCompletoChanged(bool value) => ProgramarRecarga();

    /// <summary>Un cambio nuevo cancela la espera anterior. Un texto inválido en un NumericUpDown no llega acá (el VM
    /// conserva el valor), así que no recarga con el valor viejo.</summary>
    private void ProgramarRecarga()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _tareaDebounce = DebounceCargarAsync(_debounceCts.Token);
    }

    private async Task DebounceCargarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(EsperaDebounce, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await CargarAsync();
    }

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoLibroCaja = "No tenés permiso para ver el libro caja.";

    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor no queda nada cargado: el reintento no puede mostrar el período de otra consulta.</summary>
    protected override void AlPerderConexion() => LimpiarPeriodo();

    /// <summary>Reintentar consulta el año, el mes y el modo que están en pantalla, no los de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    /// <summary>
    /// La dispara la View (DataContextChanged), Actualizar y el cambio de año, mes o modo. Protegida: un 403 deja
    /// SinPermiso (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del modal
    /// global, y mientras espera Cargando está en true. Pide turno: si mientras espera empezó otra carga (del mismo modo
    /// o del otro), su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        var anio = Anio;
        var mes = Mes;
        var anual = VerAnioCompleto;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            if (anual)
            {
                var libroAnual = await _service.ObtenerLibroCajaAnualAsync(anio);
                // El año no trae saldos: el inicial del año es el inicial de enero (sin cambiar la API).
                var enero = await _service.ObtenerLibroCajaMesAsync(anio, 1);
                if (!turno.EsLaUltima)
                    return;
                AplicarAnual(libroAnual, enero.SaldoInicial);
                return;
            }

            var libroMes = await _service.ObtenerLibroCajaMesAsync(anio, mes);
            if (!turno.EsLaUltima)
                return;
            AplicarMes(libroMes);
        }, MensajeSinPermisoLibroCaja, turno);
    }

    private void AplicarMes(LibroCajaMesDto libro)
    {
        LimpiarPeriodo();
        SaldoInicial = libro.SaldoInicial;
        SaldoFinal = libro.SaldoFinal;
        foreach (var mov in libro.Movimientos)
            Movimientos.Add(mov);
        foreach (var t in libro.TotalesPorRubro)
            TotalesPorRubro.Add(t);
        foreach (var t in libro.TotalesPorFuente)
            TotalesPorFuente.Add(t);
        LibroMes = libro;
        SinMovimientosEnElPeriodo = libro.Movimientos.Count == 0;
    }

    /// <summary>El saldo final del año es el inicial más los netos de los doce meses: la misma cuenta del saldo corrido
    /// que hace el servidor mes a mes.</summary>
    private void AplicarAnual(LibroCajaAnualDto libro, decimal saldoInicialDelAnio)
    {
        LimpiarPeriodo();
        SaldoInicial = saldoInicialDelAnio;
        SaldoFinal = saldoInicialDelAnio + libro.TotalesPorMes.Sum(t => t.Neto);
        LibroAnual = libro;
        MesesConMovimientos = libro.TotalesPorMes.Count(t => t.Ingresos != 0m || t.Egresos != 0m);
        SinMovimientosEnElPeriodo = libro.TotalesPorMes.All(t => t.Ingresos == 0m && t.Egresos == 0m);
    }

    /// <summary>Saca de pantalla el período cargado (el de un modo o el del otro).</summary>
    private void LimpiarPeriodo()
    {
        Movimientos.Clear();
        TotalesPorRubro.Clear();
        TotalesPorFuente.Clear();
        LibroMes = null;
        LibroAnual = null;
        SaldoInicial = 0m;
        SaldoFinal = 0m;
        MesesConMovimientos = 0;
        SinMovimientosEnElPeriodo = false;
    }

    public IAsyncRelayCommand RecargarCommand => field ??= ComandoDeGuardado(RecargarAsync);

    private async Task RecargarAsync() => await CargarAsync();

    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA.
    /// 10 columnas dispara apaisado automático (Tarea 4 de la plantilla).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(MovimientoCajaDto.Fecha), "Fecha"),
        new ColumnaPdf(nameof(MovimientoCajaDto.Tipo), "Tipo"),
        new ColumnaPdf(nameof(MovimientoCajaDto.Concepto), "Concepto"),
        new ColumnaPdf(nameof(MovimientoCajaDto.ProveedorNombre), "Proveedor"),
        new ColumnaPdf(nameof(MovimientoCajaDto.NumeroFactura), "Factura"),
        new ColumnaPdf(nameof(MovimientoCajaDto.FuenteNombre), "Fuente"),
        new ColumnaPdf(nameof(MovimientoCajaDto.RubroNombre), "Rubro"),
        new ColumnaPdf(nameof(MovimientoCajaDto.Ingreso), "Ingreso"),
        new ColumnaPdf(nameof(MovimientoCajaDto.Egreso), "Egreso"),
        new ColumnaPdf(nameof(MovimientoCajaDto.SaldoCorrido), "Saldo corrido"),
    };

    /// <summary>Columnas del PDF anual: las de la grilla de totales por mes, sobre la fila de presentación
    /// <see cref="FilaPdfMensual"/> (4 columnas: sale vertical). El mes va con su nombre; el déficit se marca con el
    /// signo del neto y con la sección "Meses con déficit".</summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdfAnual = new[]
    {
        new ColumnaPdf(nameof(FilaPdfMensual.Mes), "Mes"),
        new ColumnaPdf(nameof(FilaPdfMensual.Ingresos), "Ingresos"),
        new ColumnaPdf(nameof(FilaPdfMensual.Egresos), "Egresos"),
        new ColumnaPdf(nameof(FilaPdfMensual.Neto), "Neto"),
    };

    /// <summary>
    /// El exporte serializa en el cliente el período CARGADO: sin permiso, mientras carga o sin movimientos no hay nada
    /// confiable que exportar.
    /// </summary>
    public bool PuedeExportar =>
        !SinPermiso && !Cargando && (LibroMes is not null || LibroAnual is not null) && !SinMovimientosEnElPeriodo;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is nameof(SinPermiso) or nameof(Cargando) or nameof(LibroMes) or nameof(LibroAnual)
                or nameof(SinMovimientosEnElPeriodo))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }

    private string Emisor() =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema";

    /// <summary>
    /// Exporta el período CARGADO (no el de los filtros, que pueden ir por delante mientras espera la recarga): con el
    /// año completo, el PDF anual; con un mes, el PDF mensual de siempre (A4 apaisado, 10 columnas, aviso de volumen
    /// por encima de 500 filas). Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private async Task ExportarPdfAsync()
    {
        if (SinMovimientosEnElPeriodo)
            return;
        if (LibroAnual is { } anual)
        {
            await ExportarAnioAsync(anual);
            return;
        }
        if (LibroMes is { } mes)
            await ExportarMesAsync(mes);
    }

    private async Task ExportarMesAsync(LibroCajaMesDto mes)
    {
        if (!await AvisoVolumenExportacion.ConfirmarAsync(Movimientos.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Libro caja",
                DescripcionFiltros: $"Mes: {mes.Mes:00}/{mes.Anio:0000}.",
                UsuarioEmisor: Emisor());

            var resumen = new ResumenPdf(
                Totales: new[]
                {
                    new TotalPdf("Saldo inicial", SaldoInicial),
                    new TotalPdf("Saldo final", SaldoFinal),
                },
                Secciones: new[]
                {
                    new SeccionResumenPdf(
                        "Totales por rubro",
                        [.. TotalesPorRubro.Select(t => new TotalPdf(t.Clave, t.Total))]),
                    new SeccionResumenPdf(
                        "Totales por fuente",
                        [.. TotalesPorFuente.Select(t => new TotalPdf(t.Clave, t.Total))]),
                });

            var pdf = _pdfExporter.Exportar(Movimientos, ColumnasPdf, metadatos, resumen);
            await GuardarYOfrecerAbrirAsync(pdf, $"libro-caja-{mes.Anio:0000}-{mes.Mes:00}.pdf");
        }, _confirmacion);
    }

    /// <summary>PDF anual: la tabla de totales por mes y, después (la plantilla imprime el resumen después de la tabla),
    /// los saldos del año, los meses en déficit y los totales por rubro. Sin totales por fuente.</summary>
    private async Task ExportarAnioAsync(LibroCajaAnualDto anual)
    {
        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Libro caja",
                DescripcionFiltros: $"Año completo: {anual.Anio:0000}.",
                UsuarioEmisor: Emisor());

            var pdf = _pdfExporter.Exportar(ArmarFilasPdfAnual(anual.TotalesPorMes), ColumnasPdfAnual, metadatos,
                ArmarResumenAnual(anual, SaldoInicial, SaldoFinal));
            await GuardarYOfrecerAbrirAsync(pdf, $"libro-caja-{anual.Anio:0000}.pdf");
        }, _confirmacion);
    }

    private async Task GuardarYOfrecerAbrirAsync(byte[] pdf, string nombreArchivo)
    {
        using var stream = new MemoryStream(pdf);
        var guardado = await _guardado.GuardarBytesAsync(
            stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

        await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, nombreArchivo, _confirmacion, _apertura);
    }

    /// <summary>Filas de la tabla del PDF anual: el mes con su nombre (la plantilla imprime el valor tal cual); el neto
    /// conserva su signo.</summary>
    internal static IReadOnlyList<FilaPdfMensual> ArmarFilasPdfAnual(IEnumerable<TotalMensualDto> totalesPorMes) =>
        [.. totalesPorMes.Select(t => new FilaPdfMensual(NombreMesConverter.Nombre(t.Mes), t.Ingresos, t.Egresos, t.Neto))];

    /// <summary>Resumen del PDF anual. "Meses con déficit" solo si hay alguno (la plantilla no marca filas de la tabla).</summary>
    internal static ResumenPdf ArmarResumenAnual(LibroCajaAnualDto anual, decimal saldoInicial, decimal saldoFinal)
    {
        var secciones = new List<SeccionResumenPdf>();
        var deficit = anual.TotalesPorMes
            .Where(t => t.Neto < 0m)
            .Select(t => new TotalPdf(NombreMesConverter.Nombre(t.Mes), t.Neto))
            .ToList();
        if (deficit.Count > 0)
            secciones.Add(new SeccionResumenPdf("Meses con déficit", deficit));
        secciones.Add(new SeccionResumenPdf(
            "Totales por rubro", [.. anual.TotalesPorRubro.Select(t => new TotalPdf(t.Clave, t.Total))]));

        return new ResumenPdf(
            Totales: [new TotalPdf("Saldo inicial", saldoInicial), new TotalPdf("Saldo final", saldoFinal)],
            Secciones: secciones);
    }
}

/// <summary>Fila de presentación de la tabla del PDF anual: igual que <see cref="TotalMensualDto"/> pero con el mes
/// como nombre ("Marzo") en vez de número.</summary>
public sealed record FilaPdfMensual(string Mes, decimal Ingresos, decimal Egresos, decimal Neto);
