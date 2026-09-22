using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Valorización de Inventario (Inc 6).
/// Obtiene la valorización desde <see cref="IReporteStockService"/> y permite
/// exportar el resultado a CSV con un orden de columnas fijo.
/// </summary>
public partial class ValorizacionViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="ValorizacionItemDto"/>. Reutilizado por el exportador.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "ProductoId",
        "Codigo",
        "Nombre",
        "Categoria",
        "StockActual",
        "PrecioCosto",
        "ValorCosto",
    };

    /// <summary>
    /// Orden EXACTO de columnas para el export PDF (spec 2026-09-18): las de la GRILLA, no las
    /// del CSV -- excluye ProductoId (ID interno de Postgres, no significa nada en papel).
    /// Deliberadamente separada de ColumnOrder: un cambio futuro en el CSV no debe alterar el PDF.
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
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;
    private readonly IPdfExporter _pdfExporter;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly ICurrentSession _session;

    [ObservableProperty]
    private IReadOnlyList<ValorizacionItemDto> _items = new List<ValorizacionItemDto>();

    [ObservableProperty]
    private ValorizacionTotalesDto? _totales;

    public ValorizacionViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura,
        ICurrentSession session)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
        _pdfExporter = pdfExporter;
        _apertura = apertura;
        _session = session;
    }

    /// <summary>Obtiene la valorización del inventario y puebla <see cref="Items"/> y <see cref="Totales"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Obtiene la valorización del inventario y puebla <see cref="Items"/> y
    /// <see cref="Totales"/>. Público para poder engancharse desde el auto-load de la vista
    /// (<c>DataContextChanged</c> en <c>ValorizacionView.axaml.cs</c>), además de desde
    /// <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultado = await _servicio.ObtenerValorizacionAsync();
            Items = resultado.Items;
            Totales = resultado.Totales;
        }, "No tenés permiso para ver el reporte de valorización de inventario.");
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a CSV con el orden de columnas fijo y delega el guardado del archivo.
    /// No hace nada si no hay datos cargados. El guardado a disco corre bajo
    /// <see cref="ExportacionCsv"/> (bugfix 2026-08-14): un fallo DESPUÉS de elegir la ubicación
    /// (permiso denegado, disco lleno) se informa en vez de escapar del comando sin observar.
    /// </summary>
    [RelayCommand]
    private async Task ExportarAsync()
    {
        if (Items.Count == 0)
            return;

        await ExportacionCsv.EjecutarAsync(async () =>
        {
            var csv = _csvExporter.Exportar(Items, ColumnOrder);
            await _guardado.GuardarTextoAsync(csv, "valorizacion.csv");
        }, _confirmacion);
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a PDF con las columnas de la grilla, membrete institucional
    /// y aviso de volumen si supera 500 filas (spec 2026-09-18). No hace nada si no hay datos
    /// cargados. Ofrece abrir el PDF con el visor del sistema tras guardarlo.
    /// </summary>
    [RelayCommand]
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
