using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
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

    [ObservableProperty] private int _anio = DateTime.UtcNow.Year;
    [ObservableProperty] private int _mes = DateTime.UtcNow.Month;
    [ObservableProperty] private bool _verAnioCompleto;

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
        ICurrentSession session)
    {
        _service     = service;
        _guardado    = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        MovimientosView = new DataGridCollectionView(Movimientos);
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
                if (!turno.EsLaUltima)
                    return;
                AplicarAnual(libroAnual);
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

    private void AplicarAnual(LibroCajaAnualDto libro)
    {
        LimpiarPeriodo();
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

    /// <summary>
    /// Exporta <see cref="Movimientos"/> a PDF con las columnas de la grilla (A4 apaisado, 10
    /// columnas), membrete institucional y aviso de volumen si supera 500 filas (spec
    /// 2026-09-18). No hace nada si no hay datos cargados, o si la vista está en modo "año
    /// completo" (Movimientos queda vacío en ese modo -- el botón
    /// también se oculta con <c>IsVisible="{Binding !VerAnioCompleto}"</c> en el XAML).
    /// Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (VerAnioCompleto || Movimientos.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Movimientos.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Libro caja",
                DescripcionFiltros: $"Mes: {Mes:00}/{Anio:0000}.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

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
            using var stream = new MemoryStream(pdf);
            var nombreArchivo = $"libro-caja-{Anio:0000}-{Mes:00}.pdf";
            var guardado = await _guardado.GuardarBytesAsync(
                stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, nombreArchivo, _confirmacion, _apertura);
        }, _confirmacion);
    }
}
