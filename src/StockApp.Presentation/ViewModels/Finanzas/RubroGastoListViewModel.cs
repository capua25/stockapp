using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Sub-lista de rubros de gasto dentro de "Maestros de finanzas".</summary>
public partial class RubroGastoListViewModel : ViewModelBase
{
    private readonly IRubroGastoService   _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private RubroGasto? _itemSeleccionado;

    public ObservableCollection<RubroGasto> Items { get; } = new();

    public RubroGastoListViewModel(
        IRubroGastoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodosAsync();
        Items.Clear();
        foreach (var r in resultados)
            Items.Add(r);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<RubroGastoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionado = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<RubroGastoFormViewModel>(vm => vm.CargarParaEditar(seleccionado)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el rubro \"{ItemSeleccionado.Nombre}\" (código {ItemSeleccionado.Codigo})?");
        if (!confirmar) return;

        try
        {
            await _service.BajaLogicaAsync(ItemSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
