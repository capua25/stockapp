using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Opción de filtro por tipo de movimiento para el ComboBox del historial.
/// Valor=null representa "Todos" (sin filtro de tipo).
/// </summary>
public sealed record OpcionTipoMovimiento(string Nombre, TipoMovimiento? Valor);

/// <summary>
/// Opción de filtro por producto para el ComboBox del historial.
/// Valor=null representa "Todos" (sin filtro de producto).
/// </summary>
public sealed record OpcionProducto(string Nombre, Producto? Valor);

/// <summary>
/// ViewModel del historial de movimientos de stock con filtros y recálculo.
/// </summary>
public partial class MovimientoHistorialViewModel : ViewModelBase
{
    private readonly IMovimientoStockService _service;
    private readonly INavigationService      _navigation;
    private readonly IProductoService        _productoService;

    [ObservableProperty]
    private int? _filtroProductoId;

    [ObservableProperty]
    private TipoMovimiento? _filtroTipo;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    [ObservableProperty]
    private int? _productoIdParaRecalcular;

    /// <summary>Opción de producto seleccionada en el ComboBox de filtro (Valor=null = "Todos").</summary>
    [ObservableProperty]
    private OpcionProducto? _productoFiltroSeleccionado;

    /// <summary>Opción de tipo seleccionada en el ComboBox de filtro (Valor=null = "Todos").</summary>
    [ObservableProperty]
    private OpcionTipoMovimiento? _tipoFiltroSeleccionado;

    public ObservableCollection<MovimientoHistorialDto> Items { get; } = new();

    /// <summary>
    /// Vista sobre <see cref="Items"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Items, así los Clear/Add de
    /// CargarAsync/BuscarAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView ItemsView { get; }

    /// <summary>Opciones de producto disponibles para el ComboBox de filtro ("Todos" + productos activos).</summary>
    public ObservableCollection<OpcionProducto> Productos { get; } = new();

    /// <summary>Opciones fijas para el ComboBox de filtro por tipo ("Todos", "Entrada", "Salida").</summary>
    public ObservableCollection<OpcionTipoMovimiento> TiposDisponibles { get; } = new()
    {
        new OpcionTipoMovimiento("Todos", null),
        new OpcionTipoMovimiento("Entrada", TipoMovimiento.Entrada),
        new OpcionTipoMovimiento("Salida", TipoMovimiento.Salida),
    };

    public MovimientoHistorialViewModel(
        IMovimientoStockService service,
        INavigationService navigation,
        IProductoService productoService)
    {
        _service         = service;
        _navigation      = navigation;
        _productoService = productoService;

        ItemsView = new DataGridCollectionView(Items);

        _tipoFiltroSeleccionado = TiposDisponibles[0];
    }

    partial void OnProductoFiltroSeleccionadoChanged(OpcionProducto? value)
        => FiltroProductoId = value?.Valor?.Id;

    partial void OnTipoFiltroSeleccionadoChanged(OpcionTipoMovimiento? value)
        => FiltroTipo = value?.Valor;

    /// <summary>
    /// Inicialización de la vista: carga los productos activos para el filtro
    /// y el historial completo. Se invoca una sola vez al mostrar la vista
    /// (no hay hook de navegación que lo dispare, ver code-behind).
    /// </summary>
    public async Task InicializarAsync()
    {
        var productos = await _productoService.BuscarAsync(null, null, null);
        Productos.Clear();
        Productos.Add(new OpcionProducto("Todos", null));
        foreach (var p in productos.Where(p => p.Activo))
            Productos.Add(new OpcionProducto(p.Nombre, p));

        ProductoFiltroSeleccionado = Productos[0];

        await CargarAsync();
    }

    public async Task CargarAsync()
    {
        var filtro = new HistorialMovimientoFiltro();
        var resultados = await _service.ObtenerHistorialAsync(filtro);
        Items.Clear();
        foreach (var item in resultados)
            Items.Add(item);
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        var filtro = new HistorialMovimientoFiltro(
            ProductoId: FiltroProductoId,
            Tipo: FiltroTipo,
            FechaDesde: ALocalAUtc(FechaDesde),
            FechaHasta: ALocalAUtc(FechaHasta));

        var resultados = await _service.ObtenerHistorialAsync(filtro);
        Items.Clear();
        foreach (var item in resultados)
            Items.Add(item);
    }

    /// <summary>
    /// Convierte una fecha LOCAL (la que produce el <c>CalendarDatePicker</c> bindeado a
    /// FechaDesde/FechaHasta, ver XAML) a UTC antes de pasarla al filtro. El repositorio
    /// compara contra <c>MovimientoStock.Fecha</c>, persistida en UTC
    /// (<c>DateTime.UtcNow</c>) — sin esta conversión, con UTC-3 el rango queda desalineado:
    /// un movimiento de las 23:00 hora local puede caer fuera de "hasta hoy" (bug de huso
    /// horario). Contrato: <see cref="StockApp.Application.Interfaces.IMovimientoStockRepository"/>
    /// siempre recibe fechas en UTC.
    /// </summary>
    private static DateTime? ALocalAUtc(DateTime? fechaLocal)
        => fechaLocal.HasValue
            ? DateTime.SpecifyKind(fechaLocal.Value, DateTimeKind.Local).ToUniversalTime()
            : null;

    [RelayCommand]
    private async Task RecalcularAsync()
    {
        if (ProductoIdParaRecalcular is null)
            return;

        await _service.RecalcularStockAsync(ProductoIdParaRecalcular.Value);
        await CargarAsync();
    }
}
