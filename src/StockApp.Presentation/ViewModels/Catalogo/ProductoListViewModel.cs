using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de productos con búsqueda por nombre, SKU o código de barras.
/// </summary>
public partial class ProductoListViewModel : ViewModelBase
{
    private readonly IProductoService   _service;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string? _filtroBusqueda;

    public ObservableCollection<Producto> Items { get; } = new();

    public ProductoListViewModel(IProductoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.BuscarAsync(null, null, null);
        Items.Clear();
        foreach (var p in resultados)
            Items.Add(p);
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        var resultados = await _service.BuscarAsync(null, null, FiltroBusqueda);
        Items.Clear();
        foreach (var p in resultados)
            Items.Add(p);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ProductoFormViewModel>());
}
