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
/// ViewModel del reporte de Stock por Categoría (Inc 6). Obtiene el resumen
/// agrupado desde <see cref="IReporteStockService"/> y permite exportarlo a PDF.
/// Sin filtros: el botón Buscar dispara la consulta directamente.
/// </summary>
public partial class StockCategoriaViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA.
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(StockCategoriaDto.Categoria), "Categoría"),
        new ColumnaPdf(nameof(StockCategoriaDto.CantidadProductos), "Productos"),
        new ColumnaPdf(nameof(StockCategoriaDto.StockTotal), "Stock Total"),
        new ColumnaPdf(nameof(StockCategoriaDto.ValorCosto), "Valor Costo"),
    };

    private readonly IReporteStockService _servicio;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty]
    private IReadOnlyList<StockCategoriaDto> _items = new List<StockCategoriaDto>();

    private readonly INavigationService? _navegacion;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>Vuelve al hub de Reportes (primer tramo del breadcrumb). Sin navegación no hay a dónde volver.</summary>
    public IRelayCommand VolverCommand => field ??= new RelayCommand(
        () => _navegacion?.Navegar<ReportesViewModel>(), () => _navegacion is not null);

    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>
    /// El export serializa las filas ya cargadas: sin filas, tras un 403, sin conexión o con una carga en curso
    /// (las filas pueden ser de otra consulta) no se puede exportar.
    /// </summary>
    public bool PuedeExportar => Items.Count > 0 && !SinPermiso && !SinConexion && !Cargando;

    partial void OnItemsChanged(IReadOnlyList<StockCategoriaDto> value)
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

    public string TituloVacio => "No hay categorías con productos";

    public string MensajeVacio => "Cuando haya productos en una categoría, van a aparecer acá.";

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion() => Items = new List<StockCategoriaDto>();

    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    public StockCategoriaViewModel(
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

    /// <summary>Obtiene el resumen de stock por categoría y puebla <see cref="Items"/>.</summary>
    public IAsyncRelayCommand BuscarCommand => field ??= ComandoDeGuardado(BuscarAsync);

    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Obtiene el resumen de stock por categoría y puebla <see cref="Items"/>. Público para
    /// poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>StockCategoriaView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var items = await _servicio.ObtenerStockPorCategoriaAsync();
            if (!turno.EsLaUltima)
                return;
            Items = items;
        }, "No tenés permiso para ver el reporte de stock por categoría.", turno);
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
                Titulo: "Stock por categoría",
                DescripcionFiltros: "Sin filtros aplicados.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "stock-categoria.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "stock-categoria.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
