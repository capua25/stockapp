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
/// Listado de unidades de medida con alta y baja lógica. Solo Admin.
/// </summary>
public partial class UnidadMedidaListViewModel : ViewModelBase
{
    private readonly IUnidadMedidaService _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private UnidadMedida? _itemSeleccionado;

    public ObservableCollection<UnidadMedida> Items { get; } = new();

    public UnidadMedidaListViewModel(
        IUnidadMedidaService service,
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
        foreach (var u in resultados)
            Items.Add(u);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<UnidadMedidaFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    /// <summary>
    /// Da de baja la unidad seleccionada, previa confirmación del usuario. Las excepciones de
    /// dominio esperables (ej: ya está inactiva, o fue borrada por otra sesión) NO deben
    /// propagar y matar el proceso — regresión real reportada: BajaLogicaAsync lanza
    /// InvalidOperationException/KeyNotFoundException y, al no estar atrapada acá, la
    /// excepción llegaba sin manejar al dispatcher de Avalonia y crasheaba la app.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la unidad de medida \"{ItemSeleccionado.Nombre}\"?");
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
