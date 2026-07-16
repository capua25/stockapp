using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Fila de solo lectura de la grilla de gastos: aplana las navs y materializa el estado
/// CALCULADO (con la fecha de referencia del momento de la carga). También define las
/// columnas del export CSV.
/// </summary>
public sealed class GastoFila
{
    public Gasto Gasto { get; }

    public GastoFila(Gasto gasto, DateTime fechaReferencia)
    {
        Gasto = gasto;
        Estado = gasto.CalcularEstado(fechaReferencia).ToString();
    }

    public int Id => Gasto.Id;
    public DateTime Fecha => Gasto.Fecha;
    public string ProveedorNombre => Gasto.Proveedor?.Nombre ?? string.Empty;
    public string NumeroFactura => Gasto.NumeroFactura ?? string.Empty;
    public string Detalle => Gasto.Detalle;
    public string FuenteNombre => Gasto.FuenteFinanciamiento?.Nombre ?? string.Empty;
    public string RubroNombre => Gasto.RubroGasto?.Nombre ?? string.Empty;
    public string LineaPoaNombre => Gasto.LineaPoa?.Nombre ?? string.Empty;
    public decimal MontoTotal => Gasto.MontoTotal;
    public decimal TotalPagado => Gasto.TotalPagado;
    public decimal Saldo => Gasto.SaldoPendiente;
    public string Estado { get; }
}

/// <summary>
/// Pantalla "Gastos y facturas" (spec §7.1): grilla con filtros combinables y acciones
/// Nuevo / Editar / Pagos / Anular + export CSV. El filtro de estado se aplica EN MEMORIA
/// (el estado es calculado, el servidor no puede filtrarlo en SQL sin materializarlo).
/// </summary>
public partial class GastosViewModel : ViewModelBase
{
    public const string EstadoTodos = "Todos";

    private readonly IGastoService                _service;
    private readonly IProveedorService            _proveedoresService;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService           _rubrosService;
    private readonly ILineaPoaService             _lineasService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly ICsvExporter                 _csvExporter;
    private readonly IServicioGuardadoArchivo     _guardado;

    // ── Filtros ───────────────────────────────────────────────────────────────
    [ObservableProperty] private DateTimeOffset? _fechaDesde;
    [ObservableProperty] private DateTimeOffset? _fechaHasta;
    [ObservableProperty] private Proveedor? _proveedorSeleccionado;
    [ObservableProperty] private FuenteFinanciamiento? _fuenteSeleccionada;
    [ObservableProperty] private RubroGasto? _rubroSeleccionado;
    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;
    [ObservableProperty] private string _estadoSeleccionado = EstadoTodos;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(PagosCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnularCommand))]
    private GastoFila? _filaSeleccionada;

    public ObservableCollection<GastoFila> Filas { get; } = new();
    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();

    public IReadOnlyList<string> EstadosDisponibles { get; } =
        new[] { EstadoTodos, "Pendiente", "Parcial", "Pagada", "Vencida", "Anulada" };

    public GastosViewModel(
        IGastoService service,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado)
    {
        _service            = service;
        _proveedoresService = proveedoresService;
        _fuentesService     = fuentesService;
        _rubrosService      = rubrosService;
        _lineasService      = lineasService;
        _navigation         = navigation;
        _confirmacion       = confirmacion;
        _csvExporter        = csvExporter;
        _guardado           = guardado;
    }

    /// <summary>Carga combos de filtros + primer listado. La dispara la View (DataContextChanged).</summary>
    public async Task CargarAsync()
    {
        try
        {
            var proveedores = await _proveedoresService.ListarTodosAsync();
            ProveedoresDisponibles.Clear();
            foreach (var p in proveedores.Where(p => p.Activo))
                ProveedoresDisponibles.Add(p);

            var fuentes = await _fuentesService.ListarActivasAsync();
            FuentesDisponibles.Clear();
            foreach (var f in fuentes)
                FuentesDisponibles.Add(f);

            var rubros = await _rubrosService.ListarActivosAsync();
            RubrosDisponibles.Clear();
            foreach (var r in rubros)
                RubrosDisponibles.Add(r);

            var lineas = await _lineasService.ListarActivasAsync();
            LineasPoaDisponibles.Clear();
            foreach (var l in lineas)
                LineasPoaDisponibles.Add(l);

            await FiltrarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private GastoFiltro ArmarFiltro() => new(
        // DatePicker devuelve fecha local: se fija a medianoche UTC del día elegido
        // (mismo criterio que MovimientoHistorialViewModel).
        FechaDesde: FechaDesde is null
            ? null : DateTime.SpecifyKind(FechaDesde.Value.Date, DateTimeKind.Utc),
        FechaHasta: FechaHasta is null
            ? null : DateTime.SpecifyKind(FechaHasta.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc),
        ProveedorId: ProveedorSeleccionado?.Id,
        FuenteFinanciamientoId: FuenteSeleccionada?.Id,
        RubroGastoId: RubroSeleccionado?.Id,
        LineaPoaId: LineaPoaSeleccionada?.Id);

    [RelayCommand]
    private async Task FiltrarAsync()
    {
        try
        {
            var gastos = await _service.ListarAsync(ArmarFiltro());
            var ahora = DateTime.UtcNow;

            var filas = gastos.Select(g => new GastoFila(g, ahora));
            if (EstadoSeleccionado != EstadoTodos)
                filas = filas.Where(f => f.Estado == EstadoSeleccionado);

            Filas.Clear();
            foreach (var fila in filas)
                Filas.Add(fila);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task LimpiarFiltrosAsync()
    {
        FechaDesde = null;
        FechaHasta = null;
        ProveedorSeleccionado = null;
        FuenteSeleccionada = null;
        RubroSeleccionado = null;
        LineaPoaSeleccionada = null;
        EstadoSeleccionado = EstadoTodos;
        await FiltrarAsync();
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<GastoFormViewModel>());

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private async Task EditarAsync()
    {
        if (FilaSeleccionada is null) return;
        var gasto = FilaSeleccionada.Gasto;
        await Task.Run(() =>
            _navigation.Navegar<GastoFormViewModel>(vm => vm.CargarParaEditar(gasto)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private async Task PagosAsync()
    {
        if (FilaSeleccionada is null) return;
        var gasto = FilaSeleccionada.Gasto;
        await Task.Run(() =>
            _navigation.Navegar<PagosGastoViewModel>(vm => vm.CargarParaGasto(gasto)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private async Task AnularAsync()
    {
        if (FilaSeleccionada is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma anular el gasto \"{FilaSeleccionada.Detalle}\" " +
            $"(factura {FilaSeleccionada.NumeroFactura} — {FilaSeleccionada.MontoTotal})?");
        if (!confirmar) return;

        try
        {
            await _service.AnularAsync(FilaSeleccionada.Id);
            await FiltrarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(GastoFila.Fecha), nameof(GastoFila.ProveedorNombre), nameof(GastoFila.NumeroFactura),
        nameof(GastoFila.Detalle), nameof(GastoFila.FuenteNombre), nameof(GastoFila.RubroNombre),
        nameof(GastoFila.LineaPoaNombre), nameof(GastoFila.MontoTotal), nameof(GastoFila.TotalPagado),
        nameof(GastoFila.Saldo), nameof(GastoFila.Estado),
    };

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        var contenido = _csvExporter.Exportar(Filas, ColumnasCsv);
        await _guardado.GuardarTextoAsync(contenido, $"gastos-{DateTime.Now:yyyyMMdd}.csv");
    }
}
