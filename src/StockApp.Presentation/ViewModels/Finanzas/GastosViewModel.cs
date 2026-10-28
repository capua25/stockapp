using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Fila de solo lectura de la grilla de gastos: aplana las navs y materializa el estado
/// CALCULADO (con la fecha de referencia del momento de la carga). También define las
/// columnas del export PDF.
/// </summary>
public sealed class GastoFila
{
    public Gasto Gasto { get; }

    private const string SinDato = "—";

    public GastoFila(Gasto gasto, DateTime fechaReferencia)
    {
        Gasto = gasto;
        Estado = gasto.CalcularEstado(fechaReferencia).ToString();
    }

    public int Id => Gasto.Id;

    /// <summary>
    /// <see cref="Gasto.Fecha"/> es un valor date-only (medianoche UTC), NO un instante real.
    /// Se expone como <see cref="DateOnly"/> (no <see cref="DateTime"/>) a propósito: así el
    /// export PDF no lo confunde con un timestamp real y lo
    /// convierte a hora local, corriendo el día para atrás en husos negativos (bug real:
    /// grilla 16/07/2026 → export "15/07/2026 21:00:00"). Mismo criterio que sacó
    /// FechaUtcALocalConverter del binding de esta columna en GastosView.axaml.
    /// </summary>
    public DateOnly Fecha => DateOnly.FromDateTime(Gasto.Fecha);
    public string ProveedorNombre => Gasto.Proveedor?.Nombre ?? string.Empty;
    public string NumeroFactura => Gasto.NumeroFactura ?? string.Empty;
    public string Detalle => Gasto.Detalle;
    /// <summary>Caja del pago automático del contado; en crédito (o dato viejo sin pago automático) todavía no hay caja ni literal.</summary>
    public string CajaTexto => Gasto.PagoAutomatico?.Caja.Nombre() ?? SinDato;
    public string FuenteNombre => Gasto.PagoAutomatico?.FuenteFinanciamiento?.Nombre ?? SinDato;
    public string RubroNombre => Gasto.RubroGasto?.Nombre ?? string.Empty;
    public string LineaPoaNombre => Gasto.LineaPoa?.Nombre ?? string.Empty;
    public decimal MontoTotal => Gasto.MontoTotal;
    public decimal TotalPagado => Gasto.TotalPagado;
    public decimal Saldo => Gasto.SaldoPendiente;
    public string Estado { get; }
}

/// <summary>
/// Pantalla "Gastos y facturas" (spec §7.1): grilla con filtros combinables y acciones
/// Nuevo / Editar / Pagos / Anular + export PDF. El filtro de estado se aplica EN MEMORIA
/// (el estado es calculado, el servidor no puede filtrarlo en SQL sin materializarlo).
/// </summary>
public partial class GastosViewModel : ViewModelBase
{
    public const string EstadoTodos = "Todos";

    /// <summary>Texto del estado "Sin permiso" del listado: el servidor rechazó la consulta (403) o se perdió finanzas.ver
    /// con la pantalla abierta.</summary>
    public const string MensajeSinPermisoGastos = "No tenés permiso para ver los gastos.";

    private readonly IGastoService                _service;
    private readonly ICurrentSession              _session;
    private readonly IProveedorService            _proveedoresService;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService           _rubrosService;
    private readonly ILineaPoaService             _lineasService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly IServicioGuardadoArchivo     _guardado;
    private readonly IPdfExporter                 _pdfExporter;
    private readonly IServicioAperturaArchivo     _apertura;

    private readonly ConsultaVigente _consultas = new();

    /// <summary>Los combos de los filtros se piden una sola vez: Reintentar los vuelve a pedir solo si fallaron.</summary>
    private bool _combosCargados;

    /// <summary>Una consulta al servidor: su filtro y cómo se describe en el PDF. Se arma al empezar la consulta.</summary>
    private sealed record ConsultaGastos(GastoFiltro Filtro, IReadOnlyList<string> Descripcion);

    /// <summary>La consulta cuyo resultado está en pantalla (los filtros pueden ir por delante mientras espera una recarga).</summary>
    private ConsultaGastos? _consultaCargada;

    private int _cantidadVisible;

    /// <summary>Filas que deja ver el filtro de Estado (pie y vacío "sin resultados").</summary>
    public int CantidadVisible
    {
        get => _cantidadVisible;
        private set => SetProperty(ref _cantidadVisible, value);
    }

    /// <summary>Espera antes de recargar al cambiar un filtro: varios cambios seguidos (Limpiar repone siete) consultan
    /// una sola vez, con los últimos valores.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista.</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);

    /// <summary>
    /// False hasta que la carga inicial cargó y remapeó los combos: la preselección de <see cref="FiltrarPorLineaPoa"/> y
    /// el remapeo de la línea POA no programan una segunda consulta. Se arma antes de pedir el listado inicial: un filtro
    /// que el usuario cambia mientras espera ese listado programa su recarga y gana por turno.
    /// </summary>
    private bool _recargaArmada;

    /// <summary>True si lo que se ve está filtrado: algún filtro de la consulta cargada o un Estado distinto de "Todos".
    /// Elige entre "Todavía no hay gastos" y "Ningún gasto coincide con los filtros".</summary>
    public bool HayFiltrosAplicados => (_consultaCargada?.Descripcion.Count ?? 0) > 0 || EstadoSeleccionado != EstadoTodos;

    /// <summary>
    /// El export serializa en el cliente las filas ya cargadas: sin finanzas.ver no salen de la pantalla, tras un 403 son
    /// de una consulta rechazada y mientras carga pueden ser de otra consulta.
    /// </summary>
    public bool PuedeExportar => PuedeVerFinanzas && !SinPermiso && !Cargando;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SinPermiso) or nameof(Cargando))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }

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
    /// CargarAsync/RecargarAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView FilasView { get; }

    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();

    public IReadOnlyList<string> EstadosDisponibles { get; } =
        new[] { EstadoTodos, "Pendiente", "Parcial", "Pagada", "Vencida", "Anulada" };

    /// <summary>
    /// Gatea el botón "Pagos" de la vista (bugfix 2026-08-15): antes no tenía ningún gating —
    /// basta con Permisos.VerFinanzas para entrar a esta pantalla, pero
    /// GastoService.RegistrarPagoAsync/AnularPagoAsync exigen Permisos.RegistrarPagos sin
    /// condición. Un Operador con VerFinanzas pero sin RegistrarPagos veía el botón habilitado,
    /// llenaba el formulario de PagosGastoView y recién al guardar se comía un 403. Se OCULTA
    /// (no se deshabilita) siguiendo el mismo patrón que DocumentoFormViewModel.PuedeAnular/
    /// PuedeReabrir y TareaListViewModel.Puede*: IsVisible="{Binding Puede*}" en todos los
    /// botones de acción de pantalla del repo, nunca IsEnabled. Un 403 dispara
    /// ManejadorAccesoRevocado: la base notifica todos los bindings de la pantalla abierta
    /// (ViewModelBase.NotificarPermisosActualizados) y el gate se oculta sin navegar.
    /// GastosViewModel se registra AddTransient y se recrea en cada navegación a la pantalla,
    /// así que ya toma el ICurrentSession vigente en ese momento — mismo criterio que
    /// DocumentoFormViewModel.EsAdmin.
    /// </summary>
    public bool PuedeRegistrarPagos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarPagos);

    /// <summary>
    /// Gatea el botón "Anular" de la vista (bugfix 2026-08-15): antes solo estaba gateado por
    /// TieneSeleccion() (CanExecute) — nunca por permiso. GastoService.AnularAsync exige
    /// Permisos.RegistrarGastos sin condición. Mismo patrón que PuedeRegistrarPagos, calcado.
    /// </summary>
    public bool PuedeRegistrarGastos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarGastos);

    /// <summary>
    /// Gatea Exportar PDF (deuda 3c-2, decisión 2026-10-10): el export serializa en el cliente
    /// las <see cref="Filas"/> ya cargadas, sin llamar al servidor; sin <c>Permisos.VerFinanzas</c>
    /// esas filas no deberían poder salir de la pantalla. Se OCULTA (IsVisible) y además el comando
    /// no se puede ejecutar (CanExecute). Mismo patrón y misma revocación en caliente que
    /// <see cref="PuedeRegistrarGastos"/> (convención 21).
    /// </summary>
    public bool PuedeVerFinanzas =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.VerFinanzas);

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar dentro de la card.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las facturas de otra consulta.</summary>
    protected override void AlPerderConexion() => Filas.Clear();

    /// <summary>Reintentar consulta con los filtros que están en pantalla (y pide los combos si fallaron), no con los de
    /// la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();

    public GastosViewModel(
        IGastoService service,
        ICurrentSession session,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        IServicioGuardadoArchivo guardado,
        IPdfExporter pdfExporter,
        IServicioAperturaArchivo apertura)
    {
        _service            = service;
        _session            = session;
        _proveedoresService = proveedoresService;
        _fuentesService     = fuentesService;
        _rubrosService      = rubrosService;
        _lineasService      = lineasService;
        _navigation         = navigation;
        _confirmacion       = confirmacion;
        _guardado           = guardado;
        _pdfExporter        = pdfExporter;
        _apertura           = apertura;

        FilasView = new DataGridCollectionView(Filas) { Filter = EsVisible };
        Filas.CollectionChanged += (_, _) => RecalcularVisibles();
    }

    /// <summary>
    /// Precarga el filtro por línea POA (spec §7.4: doble click en Control POA abre las
    /// facturas de esa línea). Se llama ANTES de que la View dispare CargarAsync — ArmarFiltro()
    /// lee LineaPoaSeleccionada.Id sin depender de que ya esté en LineasPoaDisponibles.
    /// </summary>
    public void FiltrarPorLineaPoa(LineaPoa linea) => LineaPoaSeleccionada = linea;

    /// <summary>Predicado del filtro de Estado: la grilla, el pie y el PDF usan el mismo, así lo que se cuenta y lo que se
    /// exporta es lo que se ve.</summary>
    internal bool EsVisible(object item) =>
        item is GastoFila fila && (EstadoSeleccionado == EstadoTodos || fila.Estado == EstadoSeleccionado);

    /// <summary>Estado no viaja al servidor (es calculado): refiltra al instante, sin consulta y sin espera.</summary>
    partial void OnEstadoSeleccionadoChanged(string value) => AplicarFiltroEstado();

    partial void OnFechaDesdeChanged(DateTime? value) => ProgramarRecarga();
    partial void OnFechaHastaChanged(DateTime? value) => ProgramarRecarga();
    partial void OnProveedorSeleccionadoChanged(Proveedor? value) => ProgramarRecarga();
    partial void OnFuenteSeleccionadaChanged(FuenteFinanciamiento? value) => ProgramarRecarga();
    partial void OnRubroSeleccionadoChanged(RubroGasto? value) => ProgramarRecarga();
    partial void OnLineaPoaSeleccionadaChanged(LineaPoa? value) => ProgramarRecarga();

    /// <summary>Los seis filtros que viajan al servidor recargan solos tras la espera; un cambio nuevo cancela la anterior.</summary>
    private void ProgramarRecarga()
    {
        if (_recargaArmada)
            _tareaDebounce = Recarga.Programar(RecargarAsync);
    }

    /// <summary>Si el filtro esconde la fila seleccionada, la selección se limpia: Editar, Pagos y Anular no actúan sobre
    /// una fila que no se ve.</summary>
    private void AplicarFiltroEstado()
    {
        FilasView.Refresh();
        if (FilaSeleccionada is { } seleccionada && !EsVisible(seleccionada))
            FilaSeleccionada = null;
        RecalcularVisibles();
        OnPropertyChanged(nameof(HayFiltrosAplicados));
    }

    private void RecalcularVisibles() => CantidadVisible = Filas.Count(f => EsVisible(f));

    /// <summary>Arma la consulta con los filtros de pantalla y su descripción para el PDF (solo los activos: un PDF que no
    /// dice que está filtrado miente sobre su universo).</summary>
    private ConsultaGastos CapturarConsulta()
    {
        var partes = new List<string>();
        if (FechaDesde is not null || FechaHasta is not null)
            partes.Add($"Período: {FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}");
        if (ProveedorSeleccionado is not null)
            partes.Add($"Proveedor: {ProveedorSeleccionado.Nombre}");
        if (FuenteSeleccionada is not null)
            partes.Add($"Fuente: {FuenteSeleccionada.Nombre}");
        if (RubroSeleccionado is not null)
            partes.Add($"Rubro: {RubroSeleccionado.Nombre}");
        if (LineaPoaSeleccionada is not null)
            partes.Add($"Línea POA: {LineaPoaSeleccionada.Nombre}");
        return new ConsultaGastos(ArmarFiltro(), partes);
    }

    /// <summary>
    /// Combos de los filtros (una sola vez) y listado. La dispara la View (DataContextChanged) y Reintentar. Protegida: un
    /// 403 deja SinPermiso (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del
    /// modal global y mientras espera Cargando está en true. Con turno: si mientras espera empezó otra consulta, su
    /// resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        try
        {
            await EjecutarCargaProtegidaAsync(async () =>
            {
                if (!_combosCargados)
                {
                    await CargarCombosAsync();
                    _combosCargados = true;
                }
                _recargaArmada = true;
                await ListarYAplicarAsync(CapturarConsulta(), turno);
            }, MensajeSinPermisoGastos, turno);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        VaciarTrasUn403(turno);
    }

    private async Task CargarCombosAsync()
    {
        // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15): el servidor exige GestionarTablasMaestras para
        // ListarTodosAsync, pero un Operador de Finanzas solo tiene VerFinanzas. Los cuatro vienen filtrados a Activo=true
        // del lado del servidor.
        var proveedores = await _proveedoresService.ListarActivasAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores)
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

        // Bug real (verificación orgánica F4): la línea POA preseleccionada (FiltrarPorLineaPoa, desde Control POA) viene
        // de otra consulta y el ComboBox matchea SelectedItem por referencia, no por Id: sin este remap el filtro de datos
        // queda bien pero el combo se muestra en "Todas".
        if (LineaPoaSeleccionada is not null)
        {
            LineaPoaSeleccionada = LineasPoaDisponibles
                .FirstOrDefault(l => l.Id == LineaPoaSeleccionada.Id) ?? LineaPoaSeleccionada;
        }
    }

    /// <summary>
    /// Un 403 refrescó los permisos en caliente (convención 21): además de re-leer los <c>Puede*</c> (base), se reevalúa
    /// Exportar y, si ya no hay <c>VerFinanzas</c>, se vacía la grilla y la pantalla pasa a "Sin permiso" (si no, la grilla
    /// vacía diría "Todavía no hay gastos").
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        if (!PuedeVerFinanzas)
        {
            Filas.Clear();
            MarcarSinPermiso(MensajeSinPermisoGastos);
        }
        ExportarPdfCommand.NotifyCanExecuteChanged();
        base.NotificarPermisosActualizados();
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

    /// <summary>Recarga el listado con los filtros que están en pantalla (por cambio de filtro y tras Anular). Misma
    /// protección y mismo turno que la carga.</summary>
    private async Task RecargarAsync()
    {
        var turno = _consultas.Empezar();
        var consulta = CapturarConsulta();
        try
        {
            await EjecutarCargaProtegidaAsync(() => ListarYAplicarAsync(consulta, turno), MensajeSinPermisoGastos, turno);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        VaciarTrasUn403(turno);
    }

    private async Task ListarYAplicarAsync(ConsultaGastos consulta, ConsultaVigente.Turno turno)
    {
        var gastos = await _service.ListarAsync(consulta.Filtro);
        if (!turno.EsLaUltima)
            return;

        var ahora = DateTime.UtcNow;
        Filas.Clear();
        foreach (var gasto in gastos)
            Filas.Add(new GastoFila(gasto, ahora));
        _consultaCargada = consulta;
        OnPropertyChanged(nameof(HayFiltrosAplicados));
    }

    /// <summary>Tras un 403 de la consulta vigente la grilla no conserva filas viejas (ni exportables).</summary>
    private void VaciarTrasUn403(ConsultaVigente.Turno turno)
    {
        if (SinPermiso && turno.EsLaUltima)
            Filas.Clear();
    }

    /// <summary>Repone los siete filtros. No consulta por su cuenta: los cambios de los filtros de servidor se juntan en
    /// una sola recarga por el debounce, y Estado refiltra en memoria.</summary>
    [RelayCommand]
    private void LimpiarFiltros()
    {
        FechaDesde = null;
        FechaHasta = null;
        ProveedorSeleccionado = null;
        FuenteSeleccionada = null;
        RubroSeleccionado = null;
        LineaPoaSeleccionada = null;
        EstadoSeleccionado = EstadoTodos;
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<GastoFormViewModel>());

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    /// <summary>Un gasto ya anulado no se vuelve a anular (el servidor respondería "ya está anulado"); Editar sí lo abre,
    /// en solo lectura.</summary>
    private bool PuedeAnular() => FilaSeleccionada is { Gasto.Activo: true };

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

    [RelayCommand(CanExecute = nameof(PuedeAnular))]
    private async Task AnularAsync()
    {
        if (FilaSeleccionada is null) return;

        // Deuda A: la anulación de un gasto CON movimientos de stock asociados dispara un
        // asiento inverso que descuenta stock (GastoService.AnularAsync, rama
        // "ExistenMovimientosDeGastoAsync") — antes de Gasto.TieneMovimientosDeStock (propagado
        // end-to-end Api → ApiClient → dominio) el ViewModel no tenía con qué distinguir el
        // caso "hay stock de por medio" del caso "no hay", así que se optó por NO advertir
        // nunca antes que mentir en la mitad de los casos. Ahora que el dato está disponible
        // sin round-trip, se agrega la advertencia acá mismo, en la MISMA pregunta inicial —
        // no como un tercer diálogo separado (ver ReintentarAnulacionConPagoAutomaticoAsync).
        var tieneMovimientosDeStock = FilaSeleccionada.Gasto.TieneMovimientosDeStock;
        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma anular el gasto \"{FilaSeleccionada.Detalle}\" " +
            $"(factura {FilaSeleccionada.NumeroFactura} — {MonedaConverter.Formatear(FilaSeleccionada.MontoTotal)})?" +
            (tieneMovimientosDeStock ? " Esta anulación va a descontar el stock asociado." : string.Empty));
        if (!confirmar) return;

        var id = FilaSeleccionada.Id;
        try
        {
            await _service.AnularAsync(id);
            await RecargarAsync();
        }
        catch (AnulacionRequierePagoAutomaticoConfirmadoException ex)
        {
            // Decisión del usuario: en vez de dejar el 409 seco, se le ofrece al operador
            // confirmar la baja en cascada del pago automático de contado (spec: unificación
            // Contado ⇒ pago automático). Un pago MANUAL activo bloquea SIEMPRE con un
            // ReglaDeNegocioException genérico (Gasto.PagosAutomaticosADarDeBajaEnAnulacion) —
            // ESE no es esta excepción, así que cae en el catch de abajo sin ofrecer diálogo.
            await ReintentarAnulacionConPagoAutomaticoAsync(id, ex, tieneMovimientosDeStock);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>
    /// Reintento tras el 409 de pago automático (spec: unificación Contado ⇒ pago automático).
    /// Este diálogo es el que dispara la anulación en cascada REAL, así que tiene que decir
    /// TODO lo que va a pasar en un solo mensaje: si <paramref name="tieneMovimientosDeStock"/>
    /// es true, agrega el descuento de stock a la MISMA pregunta en vez de abrir un tercer
    /// diálogo — el operador ya vio la advertencia de stock en la pregunta inicial de
    /// <see cref="AnularAsync"/>, pero repetirla acá evita que la olvide justo antes de la
    /// acción irreversible (y evita la combinatoria de 3 PreguntarAsync seguidos que se
    /// clickean sin leer).
    /// </summary>
    private async Task ReintentarAnulacionConPagoAutomaticoAsync(
        int id, AnulacionRequierePagoAutomaticoConfirmadoException ex, bool tieneMovimientosDeStock)
    {
        var confirmar = await _confirmacion.PreguntarAsync(
            $"El gasto tiene un pago automático de contado activo por " +
            $"{MonedaConverter.Formatear(ex.MontoPagoAutomatico)}. Anular el gasto también va a " +
            "eliminar ese pago" + (tieneMovimientosDeStock ? " y a descontar el stock asociado" : string.Empty) +
            ". ¿Confirma la anulación?");
        if (!confirmar) return;

        try
        {
            await _service.AnularAsync(id, confirmarAnulacionDePagoAutomatico: true);
            await RecargarAsync();
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
            // este catch, RecargarAsync() nunca corre pero la UI queda muda — exactamente la
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

    /// <summary>
    /// Columnas del PDF (spec 2026-09-18): las 12 de la grilla, EN EL MISMO ORDEN que GastosView.axaml
    /// (lo custodia GastosViewTests.ColumnasPdf_TienenElMismoOrdenQueLasColumnasDeLaGrilla).
    /// Peor caso de la spec: apaisado automático (Tarea 4, >6 columnas) con 4 columnas de texto
    /// libre potencialmente largo (Detalle, Proveedor, Fuente, Línea POA) que se envuelven sin
    /// truncar (Tarea 5).
    /// </summary>
    public static readonly IReadOnlyList<ColumnaPdf> ColumnasPdf = new[]
    {
        new ColumnaPdf(nameof(GastoFila.Fecha), "Fecha"),
        new ColumnaPdf(nameof(GastoFila.ProveedorNombre), "Proveedor"),
        new ColumnaPdf(nameof(GastoFila.CajaTexto), "Caja"),
        new ColumnaPdf(nameof(GastoFila.FuenteNombre), "Fuente"),
        new ColumnaPdf(nameof(GastoFila.Detalle), "Detalle"),
        new ColumnaPdf(nameof(GastoFila.MontoTotal), "Monto"),
        new ColumnaPdf(nameof(GastoFila.TotalPagado), "Pagado"),
        new ColumnaPdf(nameof(GastoFila.Saldo), "Saldo"),
        new ColumnaPdf(nameof(GastoFila.Estado), "Estado"),
        new ColumnaPdf(nameof(GastoFila.NumeroFactura), "Factura"),
        new ColumnaPdf(nameof(GastoFila.RubroNombre), "Rubro"),
        new ColumnaPdf(nameof(GastoFila.LineaPoaNombre), "Línea POA"),
    };

    /// <summary>
    /// Describe los filtros del resultado EN PANTALLA: los de la consulta cargada (no los que el usuario cambió y todavía
    /// no recargaron) más el Estado vigente, que filtra en memoria y no viaja al servidor.
    /// </summary>
    private string ConstruirDescripcionFiltros()
    {
        var partes = new List<string>(_consultaCargada?.Descripcion ?? []);
        if (EstadoSeleccionado != EstadoTodos)
            partes.Add($"Estado: {EstadoSeleccionado}");

        return partes.Count == 0 ? "Sin filtros aplicados." : string.Join(" | ", partes) + ".";
    }

    /// <summary>
    /// El guardado a disco corre bajo <see cref="ExportacionPdf"/> (bugfix 2026-08-14).
    /// Exporta las filas VISIBLES (las que pasan <see cref="EsVisible"/>), no <see cref="Filas"/>: con un Estado elegido,
    /// el PDF no puede traer filas que el usuario no está viendo.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeExportar))]
    private async Task ExportarPdfAsync()
    {
        var visibles = Filas.Where(f => EsVisible(f)).ToList();
        if (visibles.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(visibles.Count, _confirmacion))
            return;

        await ExportacionPdf.EjecutarAsync(async () =>
        {
            var metadatos = new MetadatosDocumento(
                Titulo: "Gastos y facturas",
                DescripcionFiltros: ConstruirDescripcionFiltros(),
                UsuarioEmisor: _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Sistema");

            var pdf = _pdfExporter.Exportar(visibles, ColumnasPdf, metadatos);
            using var stream = new MemoryStream(pdf);
            var nombreArchivo = $"gastos-{DateTime.Now:yyyyMMdd}.pdf";
            var guardado = await _guardado.GuardarBytesAsync(
                stream, nombreArchivo, extension: "pdf", tipoMime: "application/pdf");

            await ExportacionPdf.OfrecerAbrirAsync(guardado, pdf, nombreArchivo, _confirmacion, _apertura);
        }, _confirmacion);
    }
}
