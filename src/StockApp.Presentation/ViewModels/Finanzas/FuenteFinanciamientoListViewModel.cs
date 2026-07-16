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

/// <summary>
/// Sub-lista de fuentes de financiamiento dentro de "Maestros de finanzas".
/// Alta/edición navegan al formulario; baja lógica con confirmación.
/// </summary>
public partial class FuenteFinanciamientoListViewModel : ViewModelBase
{
    private readonly IFuenteFinanciamientoService _service;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private FuenteFinanciamiento? _itemSeleccionado;

    public ObservableCollection<FuenteFinanciamiento> Items { get; } = new();

    public FuenteFinanciamientoListViewModel(
        IFuenteFinanciamientoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodasAsync();
        Items.Clear();
        foreach (var f in resultados)
            Items.Add(f);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<FuenteFinanciamientoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<FuenteFinanciamientoFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la fuente de financiamiento \"{ItemSeleccionado.Nombre}\"?");
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
