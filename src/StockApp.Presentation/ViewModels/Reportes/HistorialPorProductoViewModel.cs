using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Historial por Producto (Inc 6). Consulta el historial
/// de movimientos de un producto filtrado por rango de fechas vía
/// <see cref="IReporteStockService"/> y permite exportarlo a PDF.
/// </summary>
public partial class HistorialPorProductoViewModel : ViewModelBase
{
    /// <summary>Orden de la GRILLA --
    /// 8 columnas dispara apaisado automático (Tarea 4 de la plantilla).</summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(MovimientoHistorialDto.Fecha), "Fecha"),
        new ColumnaPdf(nameof(MovimientoHistorialDto.Tipo), "Tipo"),
        new ColumnaPdf(nameof(MovimientoHistorialDto.MotivoNombre), "Motivo"),
        new ColumnaPdf(nameof(MovimientoHistorialDto.Cantidad), "Cantidad"),
        new ColumnaPdf(nameof(MovimientoHistorialDto.PrecioUnitario), "P. Unitario"),
        new ColumnaPdf(nameof(MovimientoHistorialDto.StockAnterior), "Stock Ant."),
        new ColumnaPdf(nameof(MovimientoHistorialDto.StockNuevo), "Stock Nuevo"),
        new ColumnaPdf(nameof(MovimientoHistorialDto.Comentario), "Comentario"),
    };

    private readonly IReporteStockService _servicio;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IProductoService _productoService;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    /// <summary>
    /// PK del producto filtrado. Antes se tipeaba a mano en un NumericUpDown -- el ÚNICO
    /// filtro de esta pantalla, y un ID que no se muestra en ninguna vista de la app (bugfix
    /// 2026-08-19). Ahora se deriva de <see cref="ProductoSeleccionado"/> (AutoCompleteBox con
    /// búsqueda server-side), pero se conserva como ObservableProperty propio porque
    /// CargarAsync/ExportarAsync ya lo usan como fuente de verdad y los tests existentes lo
    /// asignan directo.
    /// </summary>
    [ObservableProperty]
    private int _productoId;

    /// <summary>
    /// Producto elegido en el AutoCompleteBox (bugfix 2026-08-19): "historial de UN producto"
    /// no admite "todos", así que no hay opción "Todos" acá (a diferencia de
    /// AuditoriaLogViewModel.UsuarioFiltroSeleccionado).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuscarCommand))]
    private ProductoDto? _productoSeleccionado;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private IReadOnlyList<MovimientoHistorialDto> _items = new List<MovimientoHistorialDto>();

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

    partial void OnItemsChanged(IReadOnlyList<MovimientoHistorialDto> value)
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

    public string TituloVacio => ProductoId == 0 ? "Elegí un producto" : "Sin movimientos en el período";

    public string MensajeVacio => ProductoId == 0
        ? "Buscá un producto por código o nombre para ver sus movimientos."
        : "Probá con otro período.";

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion()
    {
        Items = new List<MovimientoHistorialDto>();
        _descripcionCargada = null;
    }

    /// <summary>Reintentar consulta con los filtros que están en pantalla, no con los de la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    public HistorialPorProductoViewModel(
        IReporteStockService servicio,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IProductoService productoService,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session,
        INavigationService? navegacion = null)
    {
        _servicio = servicio;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _productoService = productoService;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
        _navegacion = navegacion;

        BuscarProductosAsync = BuscarProductosInternalAsync;
    }

    partial void OnProductoSeleccionadoChanged(ProductoDto? value)
        => ProductoId = value?.Id ?? 0;

    partial void OnProductoIdChanged(int value)
    {
        OnPropertyChanged(nameof(TituloVacio));
        OnPropertyChanged(nameof(MensajeVacio));
    }

    /// <summary>
    /// Delegado para <c>AutoCompleteBox.AsyncPopulator</c> del filtro de producto (bugfix
    /// 2026-08-19): búsqueda SERVER-SIDE vía IProductoService.BuscarPorTextoAsync (ILIKE sobre
    /// Codigo/CodigoBarras/Nombre). El catálogo se estima en 100-1000 productos en producción,
    /// así que NO se precarga completo — el propio AutoCompleteBox ya trae debounce
    /// (MinimumPopulateDelay) y cancela búsquedas obsoletas vía el CancellationToken.
    /// </summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarProductosAsync { get; }

    private async Task<IEnumerable<object>> BuscarProductosInternalAsync(string? texto, CancellationToken ct)
    {
        var resultados = await _productoService.BuscarPorTextoAsync(texto);
        return resultados;
    }

    /// <summary>
    /// Gatea el botón "Buscar" en la UI (bugfix 2026-08-19): sin producto seleccionado, la
    /// pantalla (cuyo ÚNICO filtro es el producto) no tiene nada sensato que consultar. Mismo
    /// criterio que MovimientoHistorialViewModel.PuedeEjecutarRecalcular.
    /// </summary>
    private bool PuedeBuscar() => ProductoSeleccionado is not null;

    /// <summary>Consulta el historial del producto filtrado y puebla <see cref="Items"/>.</summary>
    public IAsyncRelayCommand BuscarCommand => field ??= ComandoDeGuardado(BuscarAsync, PuedeBuscar);

    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Consulta el historial del producto filtrado y puebla <see cref="Items"/>. Público para
    /// poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>HistorialPorProductoView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
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

        // Sin producto no hay nada que consultar (el historial es de UN producto): la pantalla pide elegir uno.
        if (ProductoId == 0)
        {
            Items = new List<MovimientoHistorialDto>();
            _descripcionCargada = null;
            return;
        }

        var productoId = ProductoId;
        var desde = ALocalAUtc(FechaDesde);
        var hasta = ALocalAUtc(FechaHasta);
        var descripcion = ConstruirDescripcionFiltros();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var items = await _servicio.ObtenerHistorialPorProductoAsync(productoId, desde, hasta);
            if (!turno.EsLaUltima)
                return;
            Items = items;
            _descripcionCargada = descripcion;
        }, "No tenés permiso para ver el historial por producto.", turno);
    }

    /// <summary>
    /// Convierte una fecha LOCAL (la que produce el <c>CalendarDatePicker</c> bindeado a
    /// FechaDesde/FechaHasta, ver XAML) a UTC antes de pasarla al servicio. El repositorio
    /// subyacente (MovimientoStockRepository) compara contra <c>MovimientoStock.Fecha</c>,
    /// persistida en UTC — sin esta conversión, con UTC-3 el rango queda desalineado (bug de
    /// huso horario). Contrato: el servicio siempre recibe fechas en UTC.
    /// </summary>
    private static DateTime? ALocalAUtc(DateTime? fechaLocal)
        => fechaLocal.HasValue
            ? DateTime.SpecifyKind(fechaLocal.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

    /// <summary>Describe en criollo los filtros activos para el membrete del PDF (spec
    /// 2026-09-18). Sin producto o sin fechas, lo dice explícitamente: un PDF archivado sin
    /// aclarar su universo de datos no es auditable.</summary>
    private string ConstruirDescripcionFiltros()
    {
        var producto = ProductoSeleccionado is null
            ? "(sin producto)"
            : $"{ProductoSeleccionado.Codigo} - {ProductoSeleccionado.Nombre}";
        var periodo = FechaDesde is null && FechaHasta is null
            ? "Todo el histórico"
            : $"{FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}";
        return $"Producto: {producto}. Período: {periodo}.";
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a PDF con las columnas de la grilla (A4 apaisado, 8 columnas),
    /// membrete institucional y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace
    /// nada si no hay datos cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
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
                Titulo: "Historial por producto",
                DescripcionFiltros: _descripcionCargada ?? ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "historial-producto.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "historial-producto.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
