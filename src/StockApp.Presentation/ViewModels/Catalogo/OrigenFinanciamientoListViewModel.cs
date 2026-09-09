using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de orígenes de financiamiento con alta y baja lógica. Solo accesible para Admin.
/// </summary>
public partial class OrigenFinanciamientoListViewModel : ViewModelBase
{
    private readonly IOrigenFinanciamientoService _service;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private OrigenFinanciamiento? _itemSeleccionado;

    public ObservableCollection<OrigenFinanciamiento> Items { get; } = new();

    public OrigenFinanciamientoListViewModel(
        IOrigenFinanciamientoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        try
        {
            var resultados = await _service.ListarTodasAsync();
            Items.Clear();
            foreach (var o in resultados)
                Items.Add(o);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<OrigenFinanciamientoFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<OrigenFinanciamientoFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el origen de financiamiento \"{ItemSeleccionado.Nombre}\"?");
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
