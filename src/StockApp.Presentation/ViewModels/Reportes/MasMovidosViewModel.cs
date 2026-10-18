using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Productos Más Movidos (Inc 6). Consulta el top N de
/// productos por movimientos en un período vía <see cref="IReporteStockService"/>
/// y permite exportarlo a PDF.
/// </summary>
public partial class MasMovidosViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA --
    /// excluye ProductoId (ID interno de Postgres, no significa nada en papel).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(MasMovidoDto.Codigo), "Código"),
        new ColumnaPdf(nameof(MasMovidoDto.Nombre), "Nombre"),
        new ColumnaPdf(nameof(MasMovidoDto.CantidadMovimientos), "Movimientos"),
        new ColumnaPdf(nameof(MasMovidoDto.VolumenTotal), "Volumen Total"),
    };

    private readonly IReporteStockService _servicio;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private int _topN = 20;

    [ObservableProperty]
    private IReadOnlyList<MasMovidoDto> _items = new List<MasMovidoDto>();

    [ObservableProperty]
    private string? _mensajeError;

    private readonly INavigationService? _navegacion;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>Descripción de filtros de la consulta CARGADA: el PDF rotula lo que muestra la grilla, no lo que hoy dicen los filtros.</summary>
    private string? _descripcionCargada;

    /// <summary>Vuelve al hub de Reportes (primer tramo del breadcrumb). Sin navegación no hay a dónde volver.</summary>
    public IRelayCommand VolverCommand => field ??= new RelayCommand(
        () => _navegacion?.Navegar<ReportesViewModel>(), () => _navegacion is not null);

    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>
    /// El export serializa las filas ya cargadas: sin filas, tras un 403, sin conexión o con una carga en curso
    /// (las filas pueden ser de otra consulta) no se puede exportar.
    /// </summary>
    public bool PuedeExportar => Items.Count > 0 && !SinPermiso && !SinConexion && !Cargando;

    partial void OnItemsChanged(IReadOnlyList<MasMovidoDto> value)
    {
        OnPropertyChanged(nameof(PuedeExportar));
        ExportarPdfCommand.NotifyCanExecuteChanged();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SinPermiso) or nameof(SinConexion) or nameof(Cargando))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }

    public string TituloVacio => "No hubo movimientos en el período";

    public string MensajeVacio => "Probá con otro período.";

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion()
    {
        Items = new List<MasMovidoDto>();
        _descripcionCargada = null;
    }

    /// <summary>Reintentar consulta con los filtros que están en pantalla, no con los de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    public MasMovidosViewModel(
        IReporteStockService servicio,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session,
        INavigationService? navegacion = null)
    {
        _servicio = servicio;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
        _navegacion = navegacion;
    }

    /// <summary>Consulta los productos más movidos del período y puebla <see cref="Items"/>.</summary>
    public IAsyncRelayCommand BuscarCommand => field ??= ComandoDeGuardado(BuscarAsync);

    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Consulta los productos más movidos del período y puebla <see cref="Items"/>. Público
    /// para poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>MasMovidosView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        if (FechaDesde is not null && FechaHasta is not null && FechaDesde > FechaHasta)
        {
            MensajeError = "La fecha 'Desde' no puede ser posterior a 'Hasta'.";
            return;
        }

        MensajeError = null;
        var turno = _consultas.Empezar();
        var desde = ALocalAUtc(FechaDesde);
        var hasta = ALocalAUtc(FechaHasta);
        var topN = TopN;
        var descripcion = ConstruirDescripcionFiltros();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var items = await _servicio.ObtenerMasMovidosAsync(desde, hasta, topN);
            if (!turno.EsLaUltima)
                return;
            Items = items;
            _descripcionCargada = descripcion;
        }, "No tenés permiso para ver el reporte de productos más movidos.", turno);
    }

    /// <summary>
    /// Convierte una fecha LOCAL (la que produce el <c>CalendarDatePicker</c> bindeado a
    /// FechaDesde/FechaHasta, ver XAML) a UTC antes de pasarla al servicio. El repositorio
    /// subyacente (ReporteStockRepository) compara contra <c>MovimientoStock.Fecha</c>,
    /// persistida en UTC — sin esta conversión, con UTC-3 el rango queda desalineado (bug de
    /// huso horario). Contrato: el servicio siempre recibe fechas en UTC.
    /// </summary>
    private static DateTime? ALocalAUtc(DateTime? fechaLocal)
        => fechaLocal.HasValue
            ? DateTime.SpecifyKind(fechaLocal.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

    /// <summary>
    /// Describe el período y el Top N activos para el membrete del PDF. El Top N NO es
    /// paginación: es un filtro que el usuario edita (<c>NumericUpDown</c> en
    /// <c>MasMovidosView.axaml</c>), así que tiene que quedar explícito en el documento impreso
    /// -- sin esto, el PDF no dice que está recortado a los primeros N y deja de ser auditable
    /// (spec 2026-09-18). Si el rango de fechas quedó vacío (valor por defecto), el texto lo
    /// aclara igual en vez de omitirlo.
    /// </summary>
    private string ConstruirDescripcionFiltros()
    {
        var periodo = FechaDesde is null && FechaHasta is null
            ? "Todo el histórico"
            : $"Período: {FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}";
        return $"{periodo}. Top {TopN}.";
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a PDF con las columnas de la grilla, membrete institucional
    /// y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace nada si no hay datos
    /// cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private async Task ExportarPdfAsync()
    {
        if (Items.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(Items.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Productos más movidos",
                DescripcionFiltros: _descripcionCargada ?? ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "mas-movidos.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "mas-movidos.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
