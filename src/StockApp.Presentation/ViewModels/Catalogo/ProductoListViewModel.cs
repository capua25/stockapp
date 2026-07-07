using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de productos con búsqueda por nombre, SKU o código de barras.
/// </summary>
public partial class ProductoListViewModel : ViewModelBase
{
    /// <summary>Espera de debounce antes de disparar la búsqueda automática mientras se escribe.</summary>
    private const int DebounceMs = 300;

    private readonly IProductoService   _service;
    private readonly INavigationService _navigation;

    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// Tarea del debounce disparada por cada cambio de <see cref="FiltroBusqueda"/>. Expuesta
    /// como internal para que los tests puedan awaitarla de forma determinista, igual que el
    /// patrón usado en <see cref="ShellViewModel"/> para tareas de background.
    /// </summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    [ObservableProperty]
    private string? _filtroBusqueda;

    public ObservableCollection<Producto> Items { get; } = new();

    /// <summary>
    /// Vista sobre <see cref="Items"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Items, así los Clear/Add de
    /// CargarAsync/EjecutarBusquedaAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView ItemsView { get; }

    public ProductoListViewModel(IProductoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;

        ItemsView = new DataGridCollectionView(Items);
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.BuscarAsync(null, null, null);
        Items.Clear();
        foreach (var p in resultados)
            Items.Add(p);
    }

    /// <summary>
    /// Dispara la búsqueda automática con debounce cada vez que cambia el filtro, para no
    /// golpear el servicio en cada tecla. Cancela la búsqueda en curso (si la hay) antes de
    /// programar la nueva espera.
    /// </summary>
    partial void OnFiltroBusquedaChanged(string? value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _tareaDebounce = DebounceBuscarAsync(_debounceCts.Token);
    }

    private async Task DebounceBuscarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(DebounceMs, ct);
        }
        catch (OperationCanceledException)
        {
            // Búsqueda obsoleta: se tipeó de nuevo antes de que venza la espera, se descarta.
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await EjecutarBusquedaAsync();
    }

    [RelayCommand]
    private async Task BuscarAsync() => await EjecutarBusquedaAsync();

    /// <summary>
    /// Búsqueda por término único: matchea por Nombre, SKU o código de barras (lógica OR).
    /// Reusada tanto por el comando del botón como por el debounce automático.
    /// </summary>
    private async Task EjecutarBusquedaAsync()
    {
        var resultados = await _service.BuscarPorTextoAsync(FiltroBusqueda);
        Items.Clear();
        foreach (var p in resultados)
            Items.Add(p);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ProductoFormViewModel>());
}
