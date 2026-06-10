using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// ViewModel del historial de movimientos de stock con filtros y recálculo.
/// </summary>
public partial class MovimientoHistorialViewModel : ViewModelBase
{
    private readonly IMovimientoStockService _service;
    private readonly INavigationService      _navigation;

    [ObservableProperty]
    private int? _filtroProductoId;

    [ObservableProperty]
    private TipoMovimiento? _filtroTipo;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private int? _productoIdParaRecalcular;

    public ObservableCollection<MovimientoHistorialDto> Items { get; } = new();

    public MovimientoHistorialViewModel(
        IMovimientoStockService service,
        INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public async Task CargarAsync()
    {
        var filtro = new HistorialMovimientoFiltro();
        var resultados = await _service.ObtenerHistorialAsync(filtro);
        Items.Clear();
        foreach (var item in resultados)
            Items.Add(item);
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        var filtro = new HistorialMovimientoFiltro(
            ProductoId: FiltroProductoId,
            Tipo: FiltroTipo,
            FechaDesde: FechaDesde,
            FechaHasta: FechaHasta);

        var resultados = await _service.ObtenerHistorialAsync(filtro);
        Items.Clear();
        foreach (var item in resultados)
            Items.Add(item);
    }

    [RelayCommand]
    private async Task RecalcularAsync()
    {
        if (ProductoIdParaRecalcular is null)
            return;

        await _service.RecalcularStockAsync(ProductoIdParaRecalcular.Value);
        await CargarAsync();
    }
}
