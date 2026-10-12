using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Control POA" (spec §7.4): una fila por línea con presupuesto, gastado, saldo
/// y % de ejecución. Doble click abre las facturas de esa línea en GastosViewModel filtrado.
/// </summary>
public partial class ControlPoaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly INavigationService     _navigation;
    private readonly ICsvExporter           _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService   _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty] private int _ejercicio;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirGastosDeLaLineaCommand))]
    private ControlPoaLineaDto? _filaSeleccionada;

    public ObservableCollection<ControlPoaLineaDto> Filas { get; } = new();
    public DataGridCollectionView FilasView { get; }

    public ControlPoaViewModel(
        IFinanzasVistasService service, INavigationService navigation,
        ICsvExporter csvExporter, IServicioGuardadoArchivo guardado, IConfirmacionService confirmacion,
        IPdfExporter pdfExporter, IServicioAperturaArchivo apertura, ICurrentSession session,
        TimeProvider? reloj = null)
    {
        // El año arranca del reloj (inyectable para los datos fijos de los mockups). Se escribe el campo: no dispara la
        // recarga de OnEjercicioChanged, la primera carga la pide la vista.
        _ejercicio = (reloj ?? TimeProvider.System).GetUtcNow().Year;

        _service     = service;
        _navigation  = navigation;
        _csvExporter = csvExporter;
        _guardado    = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;

        FilasView = new DataGridCollectionView(Filas);
    }

    /// <summary>Espera antes de recargar al cambiar el Ejercicio con las flechas o tipeando: un año de 4 cifras son
    /// 4 cambios y solo el último tiene que ir al servidor.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista (patrón de Productos).</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private CancellationTokenSource? _debounceCts;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoControlPoa = "No tenés permiso para ver el control POA.";

    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las líneas de otra consulta.</summary>
    protected override void AlPerderConexion() => Filas.Clear();

    /// <summary>Reintentar consulta el Ejercicio que está en pantalla, no el de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    /// <summary>
    /// La dispara la View (DataContextChanged) y el cambio de Ejercicio. Protegida (403 → SinPermiso, servidor caído →
    /// SinConexion, Cargando mientras espera) y con turno: si mientras espera empezó otra carga, su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        var ejercicio = Ejercicio;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var lineas = await _service.ObtenerControlPoaAsync(ejercicio);
            if (!turno.EsLaUltima)
                return;
            Filas.Clear();
            foreach (var l in lineas)
                Filas.Add(l);
        }, MensajeSinPermisoControlPoa, turno);
    }

    /// <summary>Cambiar el Ejercicio recarga sola tras la espera; un cambio nuevo cancela la espera anterior. Un texto
    /// inválido en el NumericUpDown no llega acá (Ejercicio conserva el valor), así que no recarga con el año viejo.</summary>
    partial void OnEjercicioChanged(int value)
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

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private void AbrirGastosDeLaLinea()
    {
        if (FilaSeleccionada is null) return;
        var linea = new LineaPoa
        {
            Id = FilaSeleccionada.LineaPoaId, Nombre = FilaSeleccionada.Nombre,
            Programa = FilaSeleccionada.Programa, Ejercicio = FilaSeleccionada.Ejercicio,
        };
        _navigation.Navegar<GastosViewModel>(vm => vm.FiltrarPorLineaPoa(linea));
    }

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(ControlPoaLineaDto.Nombre), nameof(ControlPoaLineaDto.Programa), nameof(ControlPoaLineaDto.Ejercicio),
        nameof(ControlPoaLineaDto.Presupuesto), nameof(ControlPoaLineaDto.Gastado), nameof(ControlPoaLineaDto.Saldo),
        nameof(ControlPoaLineaDto.PorcentajeEjecucion), nameof(ControlPoaLineaDto.Sobregirada),
    };

    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA, no las
    /// del CSV -- excluye Ejercicio (ya está en la descripción de filtros del membrete) y
    /// Sobregirada (se ve por color/badge en la grilla, sobra como columna en papel).
    /// Deliberadamente separada de ColumnasCsv: un cambio futuro en el CSV no debe alterar el PDF.
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(ControlPoaLineaDto.Nombre), "Línea"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Programa), "Programa"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Presupuesto), "Presupuesto"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Gastado), "Gastado"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.Saldo), "Saldo"),
        new ColumnaPdf(nameof(ControlPoaLineaDto.PorcentajeEjecucion), "% Ejecución"),
    };

    /// <summary>
    /// El guardado a disco corre bajo <see cref="ExportacionCsv"/> (bugfix 2026-08-14): un fallo
    /// DESPUÉS de elegir la ubicación (permiso denegado, disco lleno) se informa en vez de
    /// escapar del comando sin observar.
    /// </summary>
    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        await ExportacionCsv.EjecutarAsync(async () =>
        {
            var contenido = _csvExporter.Exportar(Filas, ColumnasCsv);
            await _guardado.GuardarTextoAsync(contenido, $"control-poa-{Ejercicio}.csv");
        }, _confirmacion);
    }

    /// <summary>
    /// Exporta <see cref="Filas"/> a PDF con las columnas de la grilla, membrete institucional
    /// y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace nada si no hay datos
    /// cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand]
    private async Task ExportarPdfAsync()
    {
        if (Filas.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Filas.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Control POA",
                DescripcionFiltros: $"Ejercicio: {Ejercicio}.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Filas, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, $"control-poa-{Ejercicio}.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, $"control-poa-{Ejercicio}.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
