using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de categorías con alta y baja lógica. Solo accesible para Admin
/// (el menú filtra por rol; el servicio Application revalida en cada operación).
/// </summary>
public partial class CategoriaListViewModel : ViewModelBase
{
    private readonly ICategoriaService  _service;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private Categoria? _itemSeleccionado;

    public ObservableCollection<Categoria> Items { get; } = new();

    public CategoriaListViewModel(ICategoriaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodasAsync();
        Items.Clear();
        foreach (var c in resultados)
            Items.Add(c);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<CategoriaFormViewModel>());

    [RelayCommand]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;
        await _service.BajaLogicaAsync(ItemSeleccionado.Id);
        await CargarAsync();
    }
}
