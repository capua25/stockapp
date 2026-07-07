using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
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
        "PrecioVenta",
        "ValorCosto",
        "ValorVenta",
    };

    private readonly IReporteStockService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty]
    private IReadOnlyList<ValorizacionItemDto> _items = new List<ValorizacionItemDto>();

    [ObservableProperty]
    private ValorizacionTotalesDto? _totales;

    public ValorizacionViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
    }

    /// <summary>Obtiene la valorización del inventario y puebla <see cref="Items"/> y <see cref="Totales"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync()
    {
        var resultado = await _servicio.ObtenerValorizacionAsync();
        Items = resultado.Items;
        Totales = resultado.Totales;
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a CSV con el orden de columnas fijo y delega el guardado del archivo.
    /// No hace nada si no hay datos cargados.
    /// </summary>
    [RelayCommand]
    private async Task ExportarAsync()
    {
        if (Items.Count == 0)
            return;

        var csv = _csvExporter.Exportar(Items, ColumnOrder);
        await _guardado.GuardarTextoAsync(csv, "valorizacion.csv");
    }
}
