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
/// Listado de zonas con alta y baja lógica. Solo accesible para Admin
/// (el menú filtra por permiso; el servicio Application revalida en cada operación).
/// </summary>
public partial class ZonaListViewModel : ViewModelBase
{
    private readonly IZonaService         _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private Zona? _itemSeleccionado;

    public ObservableCollection<Zona> Items { get; } = new();

    public ZonaListViewModel(
        IZonaService service,
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
            foreach (var z in resultados)
                Items.Add(z);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ZonaFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<ZonaFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la zona \"{ItemSeleccionado.Nombre}\"?");
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
