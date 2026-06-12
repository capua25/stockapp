using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// ViewModel del reporte de Productos Más Movidos (Inc 6). Consulta el top N de
/// productos por movimientos en un período vía <see cref="IReporteStockService"/>
/// y permite exportarlo a CSV.
/// </summary>
public partial class MasMovidosViewModel : ViewModelBase
{
    /// <summary>
    /// Orden EXACTO de columnas para la exportación CSV. Coincide con las propiedades
    /// de <see cref="MasMovidoDto"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ColumnOrder = new[]
    {
        "ProductoId",
        "Codigo",
        "Nombre",
        "CantidadMovimientos",
        "VolumenTotal",
    };

    private readonly IReporteStockService _servicio;
    private readonly ICsvExporter _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private int _topN = 20;

    [ObservableProperty]
    private IReadOnlyList<MasMovidoDto> _items = new List<MasMovidoDto>();

    public MasMovidosViewModel(
        IReporteStockService servicio,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado)
    {
        _servicio = servicio;
        _csvExporter = csvExporter;
        _guardado = guardado;
    }

    /// <summary>Consulta los productos más movidos del período y puebla <see cref="Items"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync()
    {
        Items = await _servicio.ObtenerMasMovidosAsync(FechaDesde, FechaHasta, TopN);
    }

    /// <summary>
    /// Exporta <see cref="Items"/> a CSV con el orden de columnas fijo y delega el guardado.
    /// No hace nada si no hay datos cargados.
    /// </summary>
    [RelayCommand]
    private async Task ExportarAsync()
    {
        if (Items.Count == 0)
            return;

        var csv = _csvExporter.Exportar(Items, ColumnOrder);
        await _guardado.GuardarTextoAsync(csv, "mas-movidos.csv");
    }
}
