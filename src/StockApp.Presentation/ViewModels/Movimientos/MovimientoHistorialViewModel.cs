using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Opción de filtro por tipo de movimiento para el ComboBox del historial.
/// Valor=null representa "Todos" (sin filtro de tipo).
/// </summary>
public sealed record OpcionTipoMovimiento(string Nombre, TipoMovimiento? Valor);

/// <summary>
/// ViewModel del historial de movimientos de stock con filtros y recálculo.
/// </summary>
public partial class MovimientoHistorialViewModel : ViewModelBase
{
    private readonly IMovimientoStockService _service;
    private readonly INavigationService      _navigation;
    private readonly IProductoService        _productoService;
    private readonly IConfirmacionService    _confirmacion;
    private readonly ICurrentSession         _session;

    [ObservableProperty]
    private int? _filtroProductoId;

    [ObservableProperty]
    private TipoMovimiento? _filtroTipo;

    [ObservableProperty]
    private DateTime? _fechaDesde;

    [ObservableProperty]
    private DateTime? _fechaHasta;

    /// <summary>
    /// PK del producto a recalcular. Antes se tipeaba a mano en un NumericUpDown -- un ID que
    /// no se muestra en NINGUNA vista de la app (bugfix 2026-08-19). Ahora se deriva de
    /// <see cref="ProductoSeleccionadoParaRecalcular"/> (AutoCompleteBox con búsqueda
    /// server-side), pero se conserva como ObservableProperty propio porque RecalcularAsync ya
    /// lo usa como fuente de verdad y los tests existentes lo asignan directo.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RecalcularCommand))]
    private int? _productoIdParaRecalcular;

    /// <summary>
    /// Producto elegido en el AutoCompleteBox de "Producto a recalcular" (bugfix 2026-08-19):
    /// a diferencia de <see cref="ProductoFiltroSeleccionado"/> (que es un FILTRO y admite
    /// "Todos"), RecalcularStockAsync(int) siempre opera sobre un producto puntual -- no hay
    /// opción "Todos" acá.
    /// </summary>
    [ObservableProperty]
    private ProductoDto? _productoSeleccionadoParaRecalcular;

    /// <summary>
    /// Producto elegido en el AutoCompleteBox de filtro (bugfix 2026-08-19, migración del
    /// ComboBox con precarga completa): SIN wrapper "Todos" explícito, a diferencia del filtro
    /// de usuario de AuditoriaLogViewModel -- acá "sin selección" (null) YA ES "Todos" de forma
    /// natural (ver <see cref="OnProductoFiltroSeleccionadoChanged"/>), porque no hay una lista
    /// fija precargada donde insertar un ítem "Todos": es un campo de búsqueda, se limpia
    /// borrando el texto/la selección.
    /// </summary>
    [ObservableProperty]
    private ProductoDto? _productoFiltroSeleccionado;

    /// <summary>Opción de tipo seleccionada en el ComboBox de filtro (Valor=null = "Todos").</summary>
    [ObservableProperty]
    private OpcionTipoMovimiento? _tipoFiltroSeleccionado;

    /// <summary>
    /// Gatea la VISIBILIDAD del filtro "Producto" (bugfix 2026-08-16, familia de 323c007/
    /// 1ab2cd8, PERO variante distinta): a diferencia de Entrada/Salida e Ingreso por
    /// factura, acá el filtro es OPCIONAL, no un campo obligatorio — GET /movimientos/
    /// historial (MovimientosEndpoints.cs) exige el MISMO permiso que ya gatea el sidebar
    /// (RegistrarMovimientos), sin relación con GestionarProductos, así que el resto de la
    /// pantalla (Tipo, Desde/Hasta, la grilla) sigue siendo perfectamente usable sin él. Por
    /// eso NO se agrega GestionarProductos al gate del sidebar (eso sería esconder el
    /// historial entero a alguien que puede consultarlo).
    ///
    /// Dos momentos de detección distintos (bugfix 2026-08-19, ver
    /// <see cref="InicializarAsync"/> y <see cref="BuscarProductosParaFiltroInternalAsync"/>):
    /// (a) sin GestionarProductos en la sesión, se apaga de entrada, sin HTTP; (b) con el
    /// permiso "aparente" pero el servidor rechazando igual (403 real), se apaga recién cuando
    /// el usuario busca un producto en el AutoCompleteBox -- ya no hay una carga completa en
    /// InicializarAsync que sirva de punto único de detección temprana.
    /// </summary>
    [ObservableProperty]
    private bool _puedeFiltrarPorProducto = true;

    /// <summary>
    /// Gatea el botón "Recalcular stock" y el campo "Producto a recalcular" (bugfix
    /// 2026-08-16, hermano de <see cref="PuedeFiltrarPorProducto"/> encontrado al barrer la
    /// clase): el botón no tenía NINGÚN gating -- cualquiera que llegue a esta pantalla
    /// (RegistrarMovimientos) lo ve habilitado, pero MovimientoStockService.RecalcularStockAsync
    /// exige Permisos.RecalcularStock, un permiso DISTINTO e independiente
    /// (PermisoDependencias.cs: RecalcularStock depende de RegistrarMovimientos, no al revés).
    /// Un Operador con RegistrarMovimientos pero sin RecalcularStock generaba el mismo 403 ->
    /// modal incondicional vía AuthTokenHandler, el mismo bug que PuedeFiltrarPorProducto, solo
    /// que acá se calcula de antemano (no depende de una llamada previa que pueda fallar) por
    /// eso es una propiedad computada, no un ObservableProperty seteado post-request. Mismo
    /// patrón que GastosViewModel.PuedeRegistrarPagos: IsVisible en la vista, nunca IsEnabled.
    /// </summary>
    public bool PuedeRecalcularStock =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RecalcularStock);

    public ObservableCollection<MovimientoHistorialDto> Items { get; } = new();

    /// <summary>
    /// Vista sobre <see cref="Items"/> que habilita el ordenamiento por click en encabezados
    /// del DataGrid. Necesaria por una regresión de Avalonia 12 (AvaloniaUI/Avalonia#21129):
    /// bindear el DataGrid directo a una ObservableCollection con CanUserSortColumns="True"
    /// ya no ordena. Se crea una única vez envolviendo Items, así los Clear/Add de
    /// CargarAsync/BuscarAsync se reflejan automáticamente vía INotifyCollectionChanged.
    /// </summary>
    public DataGridCollectionView ItemsView { get; }

    /// <summary>Opciones fijas para el ComboBox de filtro por tipo ("Todos", "Entrada", "Salida").</summary>
    public ObservableCollection<OpcionTipoMovimiento> TiposDisponibles { get; } = new()
    {
        new OpcionTipoMovimiento("Todos", null),
        new OpcionTipoMovimiento("Entrada", TipoMovimiento.Entrada),
        new OpcionTipoMovimiento("Salida", TipoMovimiento.Salida),
    };

    /// <summary>
    /// Delegado para <c>AutoCompleteBox.AsyncPopulator</c> del campo "Producto a recalcular"
    /// (bugfix 2026-08-19): búsqueda SERVER-SIDE vía IProductoService.BuscarPorTextoAsync (ILIKE
    /// sobre Codigo/CodigoBarras/Nombre). El catálogo se estima en 100-1000 productos en
    /// producción, así que NO se precarga completo — el propio AutoCompleteBox ya trae debounce
    /// (MinimumPopulateDelay) y cancela búsquedas obsoletas vía el CancellationToken que recibe.
    /// Sin catch de UnauthorizedAccessException acá: este campo lo gatea
    /// <see cref="PuedeRecalcularStock"/> (Permisos.RecalcularStock), un permiso INDEPENDIENTE
    /// de GestionarProductos (que exige BuscarPorTextoAsync) -- si alguna vez se necesita
    /// manejar ese 403 puntual, tiene que ser un mecanismo propio, no compartir el catch de
    /// <see cref="BuscarProductosParaFiltroAsync"/> (que apaga <see cref="PuedeFiltrarPorProducto"/>,
    /// un flag de un campo completamente distinto).
    /// </summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarProductosAsync { get; }

    /// <summary>
    /// Delegado para <c>AutoCompleteBox.AsyncPopulator</c> del filtro "Producto" de la barra de
    /// arriba (bugfix 2026-08-19): migración del ComboBox con precarga completa
    /// (IProductoService.BuscarAsync en InicializarAsync) a búsqueda server-side, mismo
    /// mecanismo que <see cref="BuscarProductosAsync"/> pero en un delegado APARTE porque el
    /// manejo de errores es distinto (ver el catch de abajo y el comentario de
    /// <see cref="InicializarAsync"/>).
    /// </summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarProductosParaFiltroAsync { get; }

    public MovimientoHistorialViewModel(
        IMovimientoStockService service,
        INavigationService navigation,
        IProductoService productoService,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _service         = service;
        _navigation      = navigation;
        _productoService = productoService;
        _confirmacion    = confirmacion;
        _session         = session;

        ItemsView = new DataGridCollectionView(Items);

        _tipoFiltroSeleccionado = TiposDisponibles[0];

        BuscarProductosAsync = BuscarProductosInternalAsync;
        BuscarProductosParaFiltroAsync = BuscarProductosParaFiltroInternalAsync;
    }

    /// <summary>
    /// "Sin selección" ya ES "Todos" (bugfix 2026-08-19): a diferencia del ComboBox viejo, que
    /// necesitaba un ítem "Todos" explícito porque SIEMPRE hay algo seleccionado en un
    /// ComboBox, un AutoCompleteBox sin selección (SelectedItem=null, texto vacío o borrado)
    /// es un estado perfectamente natural, y FiltroProductoId=null YA significa "sin filtro de
    /// producto" en <see cref="BuscarAsync"/> (HistorialMovimientoFiltro.ProductoId=null trae
    /// todo el historial). No hace falta modelar "Todos" como un valor propio.
    /// </summary>
    partial void OnProductoFiltroSeleccionadoChanged(ProductoDto? value)
        => FiltroProductoId = value?.Id;

    partial void OnTipoFiltroSeleccionadoChanged(OpcionTipoMovimiento? value)
        => FiltroTipo = value?.Valor;

    partial void OnProductoSeleccionadoParaRecalcularChanged(ProductoDto? value)
        => ProductoIdParaRecalcular = value?.Id;

    private async Task<IEnumerable<object>> BuscarProductosInternalAsync(string? texto, CancellationToken ct)
    {
        var resultados = await _productoService.BuscarPorTextoAsync(texto);
        return resultados;
    }

    /// <summary>
    /// Bugfix 2026-08-19 (evolución del fix 2026-08-16, ver <see cref="InicializarAsync"/>):
    /// sin la carga completa de InicializarAsync ya no hay un único punto donde detectar el
    /// 403 REAL del servidor (sesión con el permiso "aparente" pero el servidor igual rechaza
    /// -- permiso revocado entre el chequeo y la llamada). Se detecta acá, en el momento en que
    /// el usuario efectivamente busca: si BuscarPorTextoAsync rechaza, se oculta el filtro
    /// reactivamente (<see cref="PuedeFiltrarPorProducto"/>=false) y se devuelve una lista
    /// vacía para que el AutoCompleteBox no explote (no propaga -- ver
    /// AutoCompleteBox.PopulateAsync, que solo atrapa TaskCanceledException, cualquier otra
    /// excepción quedaría sin observar).
    /// </summary>
    private async Task<IEnumerable<object>> BuscarProductosParaFiltroInternalAsync(string? texto, CancellationToken ct)
    {
        try
        {
            var resultados = await _productoService.BuscarPorTextoAsync(texto);
            return resultados;
        }
        catch (UnauthorizedAccessException)
        {
            PuedeFiltrarPorProducto = false;
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Inicialización de la vista: decide si el filtro "Producto" queda visible y carga el
    /// historial completo. Se invoca una sola vez al mostrar la vista (no hay hook de
    /// navegación que lo dispare, ver code-behind).
    /// </summary>
    /// <remarks>
    /// Bugfix 2026-08-16 (reporte real, opcombo/Combo2026!): GET /productos exige
    /// GestionarProductos, permiso que el sidebar NO exige para esta pantalla (solo
    /// RegistrarMovimientos, igual que GET /movimientos/historial). La primera versión de este
    /// fix atrapaba UnauthorizedAccessException acá (llamar-y-atrapar): evitaba el crash, pero
    /// NO evitaba el modal "No tenés permiso para esta operación", porque
    /// AuthTokenHandler.SendAsync dispara ApiSession.DispararAccesoRevocado() de forma
    /// INCONDICIONAL ante cualquier 403, en la capa de transporte, ANTES de que la excepción
    /// llegue a un catch acá (ver deuda documentada en AuthTokenHandler.SendAsync). Por eso se
    /// CONSULTA el permiso ANTES de mostrar el filtro (mismo mecanismo que
    /// ShellMainViewModel.Puede* / GastosViewModel.PuedeRegistrarPagos: ICurrentSession.RolActual
    /// + PermisosActuales): si no lo tiene, jamás se genera el 403, así que AuthTokenHandler
    /// nunca lo anuncia.
    ///
    /// Bugfix 2026-08-19 (evolución del fix anterior -- ComboBox con precarga completa
    /// reemplazado por AutoCompleteBox de búsqueda server-side, ver
    /// <see cref="BuscarProductosParaFiltroAsync"/>): la versión ANTERIOR de este método hacía
    /// una llamada real a IProductoService.BuscarAsync ACÁ (con el permiso ya confirmado por
    /// sesión) para precargar el catálogo completo, y el catch de UnauthorizedAccessException
    /// de ESA llamada era la red de contención que detectaba el 403 real del servidor (permiso
    /// revocado entre el chequeo de sesión y la llamada). Esa llamada YA NO EXISTE: cargar el
    /// catálogo entero de antemano es EXACTAMENTE el problema que este fix elimina (el usuario
    /// estima 100-1000 productos en producción). Sin una llamada eager, no hay ningún punto en
    /// InicializarAsync donde detectar ese 403 real -- PuedeFiltrarPorProducto queda en TRUE de
    /// forma OPTIMISTA (el chequeo de sesión, sin HTTP, es la única señal disponible acá). La
    /// red de contención se movió al lugar donde SÍ hay una llamada real al servidor: la
    /// primera búsqueda efectiva del usuario, ver el catch en
    /// <see cref="BuscarProductosParaFiltroInternalAsync"/>. Trade-off documentado: con esto,
    /// el filtro puede aparecer visible un instante y ocultarse recién cuando el usuario
    /// busca (en vez de estar oculto desde el arranque como antes) -- inevitable sin volver a
    /// pagar el costo de la precarga completa que motivó este cambio.
    /// </remarks>
    public async Task InicializarAsync()
    {
        var puedeVerProductos =
            _session.RolActual == RolUsuario.Admin ||
            _session.PermisosActuales.Contains(Permisos.GestionarProductos);

        PuedeFiltrarPorProducto = puedeVerProductos;

        // Fix (bugfix "pantalla muda ante un 403"): InicializarAsync llamaba a CargarAsync() sin
        // que NINGUNO de los dos atrapara UnauthorizedAccessException -- caso real que motivó que
        // el guardián siga la cadena de llamadas locales, no solo el método de entrada.
        await EjecutarCargaProtegidaAsync(CargarAsync, "No tenés permiso para ver el historial de movimientos.");
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

    /// <summary>
    /// Gatea el botón "Recalcular stock" en la UI (bugfix 2026-08-19): sin selección, apretar
    /// el botón antes hacía RecalcularAsync retornar en silencio -- ninguna señal de que "no
    /// pasó nada" porque no había producto elegido. Mismo criterio que
    /// MovimientoRegistroViewModelBase con ProductoSeleccionado != null. El guard interno de
    /// RecalcularAsync se conserva igual (ExecuteAsync invocado directo bypasea CanExecute).
    /// </summary>
    private bool PuedeEjecutarRecalcular() => ProductoIdParaRecalcular is not null;

    /// <summary>
    /// ProductoIdParaRecalcular se tipea a mano en un campo libre, sin relación con el filtro
    /// activo de la grilla — si ese producto no está en el resultado filtrado (caso normal),
    /// CargarAsync no cambia una sola fila y el click queda sin ninguna señal (reporte de uso
    /// real). Mismo mecanismo que PanelPermisosViewModel.GuardarAsync: informa éxito y error
    /// puntual vía IConfirmacionService.InformarAsync.
    /// </summary>
    /// <remarks>
    /// Bugfix 2026-08-16 (hermano de InicializarAsync, encontrado al barrer la clase): el gate
    /// de entrada previo (<see cref="PuedeRecalcularStock"/>) es el chequeo PRINCIPAL — evita
    /// generar el 403 que dispararía el modal incondicional de AuthTokenHandler. El catch de
    /// UnauthorizedAccessException se CONSERVA como red de contención (el permiso puede
    /// revocarse entre el chequeo y la llamada), no como mecanismo principal.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(PuedeEjecutarRecalcular))]
    private async Task RecalcularAsync()
    {
        if (ProductoIdParaRecalcular is null || !PuedeRecalcularStock)
            return;

        try
        {
            await _service.RecalcularStockAsync(ProductoIdParaRecalcular.Value);
            await CargarAsync();
            await _confirmacion.InformarAsync("Stock recalculado.");
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
