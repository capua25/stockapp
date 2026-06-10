using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de unidades de medida con alta y baja lógica. Solo Admin.
/// </summary>
public partial class UnidadMedidaListViewModel : ViewModelBase
{
    private readonly IUnidadMedidaService _service;
    private readonly INavigationService   _navigation;

    [ObservableProperty]
    private UnidadMedida? _itemSeleccionado;

    public ObservableCollection<UnidadMedida> Items { get; } = new();

    public UnidadMedidaListViewModel(IUnidadMedidaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodasAsync();
        Items.Clear();
        foreach (var u in resultados)
            Items.Add(u);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<UnidadMedidaFormViewModel>());

    [RelayCommand]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;
        await _service.BajaLogicaAsync(ItemSeleccionado.Id);
        await CargarAsync();
    }
}
