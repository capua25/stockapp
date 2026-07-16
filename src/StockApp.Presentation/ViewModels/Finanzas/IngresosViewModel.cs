using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Ingresos de caja" (spec §7.2): ABM simple de partidas, multas, préstamos.
/// Alta/edición navegan al formulario; baja lógica con confirmación.
/// </summary>
public partial class IngresosViewModel : ViewModelBase
{
    private readonly IIngresoCajaService  _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private IngresoCaja? _itemSeleccionado;

    public ObservableCollection<IngresoCaja> Items { get; } = new();

    /// <summary>
    /// Vista sobre <see cref="Items"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Items, así los Clear/Add de
    /// CargarAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView ItemsView { get; }

    public IngresosViewModel(
        IIngresoCajaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;

        ItemsView = new DataGridCollectionView(Items);
    }

    public async Task CargarAsync()
    {
        try
        {
            var resultados = await _service.ListarTodosAsync();
            Items.Clear();
            foreach (var ingreso in resultados)
                Items.Add(ingreso);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<IngresoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionado = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<IngresoFormViewModel>(vm => vm.CargarParaEditar(seleccionado)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el ingreso \"{ItemSeleccionado.Concepto}\" " +
            $"({MonedaConverter.Formatear(ItemSeleccionado.Monto)})?");
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
