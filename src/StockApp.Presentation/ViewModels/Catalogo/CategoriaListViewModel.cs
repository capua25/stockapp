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
/// Listado de categorías con alta y baja lógica. Solo accesible para Admin
/// (el menú filtra por rol; el servicio Application revalida en cada operación).
/// </summary>
public partial class CategoriaListViewModel : ViewModelBase
{
    private readonly ICategoriaService    _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private Categoria? _itemSeleccionado;

    public ObservableCollection<Categoria> Items { get; } = new();

    public CategoriaListViewModel(
        ICategoriaService service,
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
        foreach (var c in resultados)
            Items.Add(c);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<CategoriaFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    // Misma condición que PuedeDarBaja: solo se edita una categoría seleccionada y activa.
    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<CategoriaFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    /// <summary>
    /// Da de baja la categoría seleccionada, previa confirmación del usuario. Las excepciones
    /// de dominio esperables (ej: ya está inactiva, o fue borrada por otra sesión) NO deben
    /// propagar y matar el proceso — regresión real reportada: BajaLogicaAsync lanza
    /// ReglaDeNegocioException/EntidadNoEncontradaException y, al no estar atrapada acá, la
    /// excepción llegaba sin manejar al dispatcher de Avalonia y crasheaba la app.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la categoría \"{ItemSeleccionado.Nombre}\"?");
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
