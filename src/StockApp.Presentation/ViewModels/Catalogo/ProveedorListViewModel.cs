using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de proveedores con alta y baja lógica. Solo Admin.
/// </summary>
public partial class ProveedorListViewModel : ViewModelBase
{
    private readonly IProveedorService  _service;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private Proveedor? _itemSeleccionado;

    public ObservableCollection<Proveedor> Items { get; } = new();

    public ProveedorListViewModel(IProveedorService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodosAsync();
        Items.Clear();
        foreach (var p in resultados)
            Items.Add(p);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ProveedorFormViewModel>());

    [RelayCommand]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;
        await _service.BajaLogicaAsync(ItemSeleccionado.Id);
        await CargarAsync();
    }
}
