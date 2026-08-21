using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Stock por Categoría (Inc 6). Obtiene el resumen
/// agrupado desde <see cref="IReporteStockService"/> y permite exportarlo a CSV.
/// Sin filtros: el botón Buscar dispara la consulta directamente.
/// </summary>
public partial class StockCategoriaViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="StockCategoriaDto"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "Categoria",
        "CantidadProductos",
        "StockTotal",
        "ValorCosto",
        "ValorVenta",
    };

    private readonly IReporteStockService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    private IReadOnlyList<StockCategoriaDto> _items = new List<StockCategoriaDto>();

    public StockCategoriaViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado,
        IConfirmacionService confirmacion)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
        _confirmacion = confirmacion;
    }

    /// <summary>Obtiene el resumen de stock por categoría y puebla <see cref="Items"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Obtiene el resumen de stock por categoría y puebla <see cref="Items"/>. Público para
    /// poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c> en
    /// <c>StockCategoriaView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            Items = await _servicio.ObtenerStockPorCategoriaAsync();
        }, "No tenés permiso para ver el reporte de stock por categoría.");
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a CSV con el orden de columnas fijo y delega el guardado.
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
            await _guardado.GuardarTextoAsync(csv, "stock-categoria.csv");
        }, _confirmacion);
    }
}
