using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Converters;
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

    /// <summary>
    /// <see cref="Gasto.Fecha"/> es un valor date-only (medianoche UTC), NO un instante real.
    /// Se expone como <see cref="DateOnly"/> (no <see cref="DateTime"/>) a propósito: así el
    /// export CSV (<see cref="CsvExporter"/>) no lo confunde con un timestamp real y lo
    /// convierte a hora local, corriendo el día para atrás en husos negativos (bug real:
    /// grilla 16/07/2026 → CSV "15/07/2026 21:00:00"). Mismo criterio que sacó
    /// FechaUtcALocalConverter del binding de esta columna en GastosView.axaml.
    /// </summary>
    public DateOnly Fecha => DateOnly.FromDateTime(Gasto.Fecha);
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
    [ObservableProperty] private DateTime? _fechaDesde;
    [ObservableProperty] private DateTime? _fechaHasta;
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

    /// <summary>
    /// Vista sobre <see cref="Filas"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Filas, así los Clear/Add de
    /// CargarAsync/FiltrarAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView FilasView { get; }

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

        FilasView = new DataGridCollectionView(Filas);
    }

    /// <summary>
    /// Precarga el filtro por línea POA (spec §7.4: doble click en Control POA abre las
    /// facturas de esa línea). Se llama ANTES de que la View dispare CargarAsync — ArmarFiltro()
    /// lee LineaPoaSeleccionada.Id sin depender de que ya esté en LineasPoaDisponibles.
    /// </summary>
    public void FiltrarPorLineaPoa(LineaPoa linea) => LineaPoaSeleccionada = linea;

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

            // Bug real (verificación orgánica F4): si ya había una línea POA preseleccionada
            // (FiltrarPorLineaPoa, navegación desde Control POA), la instancia viene de otra
            // consulta y el ComboBox matchea SelectedItem por referencia, no por Id — sin este
            // remap el filtro de datos queda correcto pero el combo se muestra en "Todas".
            if (LineaPoaSeleccionada is not null)
            {
                LineaPoaSeleccionada = LineasPoaDisponibles
                    .FirstOrDefault(l => l.Id == LineaPoaSeleccionada.Id) ?? LineaPoaSeleccionada;
            }

            await FiltrarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private GastoFiltro ArmarFiltro() => new(
        // CalendarDatePicker devuelve la fecha elegida sin componente horario: se fija a
        // medianoche UTC del día elegido, SIN conversión de huso horario (a diferencia de
        // MovimientoHistorialViewModel, que sí convierte local→UTC porque ahí Fecha es un
        // instante real — acá el dominio de Finanzas no tiene componente horario).
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
            $"(factura {FilaSeleccionada.NumeroFactura} — {MonedaConverter.Formatear(FilaSeleccionada.MontoTotal)})?");
        if (!confirmar) return;

        var id = FilaSeleccionada.Id;
        try
        {
            await _service.AnularAsync(id);
            await FiltrarAsync();
        }
        catch (AnulacionRequierePagoAutomaticoConfirmadoException ex)
        {
            // Decisión del usuario: en vez de dejar el 409 seco, se le ofrece al operador
            // confirmar la baja en cascada del pago automático de contado (spec: unificación
            // Contado ⇒ pago automático). Un pago MANUAL activo bloquea SIEMPRE con un
            // ReglaDeNegocioException genérico (Gasto.PagosAutomaticosADarDeBajaEnAnulacion) —
            // ESE no es esta excepción, así que cae en el catch de abajo sin ofrecer diálogo.
            await ReintentarAnulacionConPagoAutomaticoAsync(id, ex);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>
    /// NOTA (deuda NO cerrada): esta advertencia solo cubre el pago automático. La anulación de
    /// un gasto CON movimientos de stock asociados dispara un asiento inverso que descuenta
    /// stock (GastoService.AnularAsync, rama "ExistenMovimientosDeGastoAsync") — deuda conocida
    /// del proyecto ("el diálogo de anulación de Gastos no advierte que ahora descuenta stock").
    /// No se cierra acá: ni Gasto ni GastoDto/GastoWire exponen si el gasto tiene movimientos
    /// asociados, y el 409 de este flujo tampoco lo informa — el ViewModel no tiene con qué
    /// distinguir el caso "hay stock de por medio" del caso "no hay", así que mostrar la
    /// advertencia siempre sería mentir en la mitad de los casos. Cerrarla requiere un campo
    /// nuevo end-to-end (Api → ApiClient → dominio), fuera del alcance de este cambio.
    /// </summary>
    private async Task ReintentarAnulacionConPagoAutomaticoAsync(
        int id, AnulacionRequierePagoAutomaticoConfirmadoException ex)
    {
        var confirmar = await _confirmacion.PreguntarAsync(
            $"El gasto tiene un pago automático de contado activo por " +
            $"{MonedaConverter.Formatear(ex.MontoPagoAutomatico)}. Anular el gasto también va a " +
            "eliminar ese pago. ¿Confirma la anulación?");
        if (!confirmar) return;

        try
        {
            await _service.AnularAsync(id, confirmarAnulacionDePagoAutomatico: true);
            await FiltrarAsync();
        }
        catch (Exception ex2) when (ex2 is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex2.Message);
        }
        catch (ServidorNoDisponibleException ex2)
        {
            // Mismo motivo que Fix 5 de IngresoPorFacturaViewModel.GuardarInternoAsync: este
            // reintento corre dentro de AnularCommand (AsyncRelayCommand) — una excepción no
            // capturada acá NO llega al handler global de App.axaml.cs (que solo cubre
            // Dispatcher.UIThread.UnhandledException), termina en
            // TaskScheduler.UnobservedTaskException → crash.log, sin avisar al operador. Sin
            // este catch, FiltrarAsync() nunca corre pero la UI queda muda — exactamente la
            // falla silenciosa de sesión expirada que ya hubo en este proyecto.
            await _confirmacion.InformarAsync(ex2.Message);
        }
        catch (UnauthorizedAccessException)
        {
            await _confirmacion.InformarAsync(
                "La sesión expiró o no tenés permiso para esta operación. " +
                "Volvé a iniciar sesión e intentá de nuevo.");
        }
        catch (Exception)
        {
            await _confirmacion.InformarAsync(
                "Ocurrió un error inesperado al anular el gasto. Si el problema persiste, contactá a soporte.");
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
