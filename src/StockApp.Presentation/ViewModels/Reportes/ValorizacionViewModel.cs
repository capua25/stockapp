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
/// ViewModel del reporte de Valorización de Inventario (Inc 6).
/// Obtiene la valorización desde <see cref="IReporteStockService"/> y permite
/// exportar el resultado a PDF.
/// </summary>
public partial class ValorizacionViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA --
    /// excluye ProductoId (ID interno de Postgres, no significa nada en papel).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(ValorizacionItemDto.Codigo), "Código"),
        new ColumnaPdf(nameof(ValorizacionItemDto.Nombre), "Nombre"),
        new ColumnaPdf(nameof(ValorizacionItemDto.Categoria), "Categoría"),
        new ColumnaPdf(nameof(ValorizacionItemDto.StockActual), "Stock"),
        new ColumnaPdf(nameof(ValorizacionItemDto.PrecioCosto), "P. Costo"),
        new ColumnaPdf(nameof(ValorizacionItemDto.ValorCosto), "Valor Costo"),
    };

    private readonly IReporteStockService _servicio;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty]
    private IReadOnlyList<ValorizacionItemDto> _items = new List<ValorizacionItemDto>();

    [ObservableProperty]
    private ValorizacionTotalesDto? _totales;

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

    partial void OnItemsChanged(IReadOnlyList<ValorizacionItemDto> value)
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

    public string TituloVacio => "No hay productos para valorizar";

    public string MensajeVacio => "Cuando haya productos activos, su valor va a aparecer acá.";

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las filas de la consulta anterior.</summary>
    protected override void AlPerderConexion()
    {
        Items = new List<ValorizacionItemDto>();
        Totales = null;
    }

    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    public ValorizacionViewModel(
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

    /// <summary>Obtiene la valorización del inventario y puebla <see cref="Items"/> y <see cref="Totales"/>.</summary>
    public IAsyncRelayCommand BuscarCommand => field ??= ComandoDeGuardado(BuscarAsync);

    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Obtiene la valorización del inventario y puebla <see cref="Items"/> y
    /// <see cref="Totales"/>. Público para poder engancharse desde el auto-load de la vista
    /// (<c>DataContextChanged</c> en <c>ValorizacionView.axaml.cs</c>), además de desde
    /// <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultado = await _servicio.ObtenerValorizacionAsync();
            if (!turno.EsLaUltima)
                return;
            Items = resultado.Items;
            Totales = resultado.Totales;
        }, "No tenés permiso para ver el reporte de valorización de inventario.", turno);
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
                Titulo: "Valorización de inventario",
                DescripcionFiltros: "Sin filtros aplicados.",
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var resumen = new ResumenPdf(new[]
            {
                new TotalPdf("Total Valor Costo", Totales?.TotalValorCosto ?? 0m),
            });

            var pdf = _pdfExporter.Exportar(Items, ColumnasPdf, metadatos, resumen);
            using var stream = new MemoryStream(pdf);
            var guardado = await _guardado.GuardarBytesAsync(
                stream, "valorizacion.pdf", extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, "valorizacion.pdf", _confirmacion, _apertura);
        }, _confirmacion);
    }
}
