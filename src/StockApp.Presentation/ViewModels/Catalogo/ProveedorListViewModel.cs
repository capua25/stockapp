using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de proveedores con alta y baja lógica. Solo Admin.
/// </summary>
public partial class ProveedorListViewModel : ViewModelBase
{
    private readonly IProveedorService    _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private Proveedor? _itemSeleccionado;

    public ObservableCollection<Proveedor> Items { get; } = new();

    public ProveedorListViewModel(
        IProveedorService service,
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
        foreach (var p in resultados)
            Items.Add(p);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ProveedorFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    /// <summary>
    /// Da de baja el proveedor seleccionado, previa confirmación del usuario. Las excepciones
    /// de dominio esperables (ej: ya está inactivo, o fue borrado por otra sesión) NO deben
    /// propagar y matar el proceso — regresión real reportada: BajaLogicaAsync lanza
    /// InvalidOperationException/KeyNotFoundException y, al no estar atrapada acá, la
    /// excepción llegaba sin manejar al dispatcher de Avalonia y crasheaba la app.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el proveedor \"{ItemSeleccionado.Nombre}\"?");
        if (!confirmar) return;

        try
        {
            await _service.BajaLogicaAsync(ItemSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
