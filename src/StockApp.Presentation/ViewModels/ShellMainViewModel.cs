using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// ViewModel del shell principal post-login. Hostea el menú lateral y la región de contenido.
/// La mayoría de los ítems del menú (Productos, Movimientos, Tareas, Finanzas, Tablas maestras,
/// Reportes) se gatean por permiso configurable (propiedades <c>Puede*</c>, Task 14, spec
/// 2026-08-10) contra <see cref="ICurrentSession.PermisosActuales"/>, no por rol fijo. Solo los
/// 4 permisos estructurales (Importación, Administración de usuarios, Diagnóstico) siguen atados
/// a <see cref="EsAdmin"/>, porque un Operador nunca puede tenerlos.
/// </summary>
public partial class ShellMainViewModel : ViewModelBase
{
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;
    private readonly IInfoApp           _infoApp;
    private readonly IConfirmacionService _confirmacion;
    private readonly IAuthService       _authService;
    private readonly IServicioPreferenciasSidebar _preferencias;

    /// <summary>
    /// Task del refresco de permisos disparado por la última navegación (spec decisión 7).
    /// internal (+ InternalsVisibleTo) para que los tests lo esperen de forma determinista —
    /// mismo patrón que PanelPermisosViewModel._tareaCarga (Task 13): sin esto, un test no
    /// tiene forma de saber cuándo terminó el fire-and-forget sin Task.Delay.
    /// </summary>
    internal Task _tareaRefrescoPermisos = Task.CompletedTask;

    /// <summary>
    /// Se dispara cuando el usuario confirma "Cerrar sesión" y la sesión ya fue limpiada
    /// (ICurrentSession.CerrarSesion()). La composition root (ShellViewModel) lo cablea a
    /// la navegación de vuelta al login, igual que BloqueoLicenciaViewModel.LicenciaActivada
    /// o ResetAdminViewModel.Volver.
    /// </summary>
    public event Action? CerrarSesionSolicitado;

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    // ── Gating por permiso configurable (spec 2026-08-10) ─────────────────────
    // Misma condición que evalúa AuthorizationService.Verificar del lado servidor: Admin
    // siempre pasa, Operador según PermisosActuales. Esto es cosmética, no seguridad — la
    // autorización real vive en las dos barreras de la API (HTTP + Application); si el binding
    // tuviera un bug y mostrara un ítem de más, el peor caso es un clic que rebota con 403.

    public bool PuedeGestionarProductos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarProductos);

    public bool PuedeRegistrarMovimientos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.RegistrarMovimientos);

    public bool PuedeGestionarTareas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTareas);

    public bool PuedeGestionarDocumentos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarDocumentos);

    public bool PuedeVerFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerFinanzas);

    public bool PuedeGestionarMaestrosFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarMaestrosFinanzas);

    public bool PuedeGestionarTablasMaestras =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTablasMaestras);

    public bool PuedeVerReportes =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerReportes);

    /// <summary>
    /// Ingreso por factura (fix bug de coherencia de permisos, 2026-08-15; ampliado 2026-08-16
    /// tras auditoría): a diferencia de las demás Puede*, combina CUATRO permisos porque el
    /// flujo real los exige los cuatro — RegistrarMovimientos y RegistrarGastos (ambos
    /// verificados sin condición por IngresoPorFacturaService.RegistrarAsync/AnularLoteAsync),
    /// VerFinanzas (sin él, los combos de fuente/rubro/línea POA de la pantalla quedan vacíos —
    /// FuenteFinanciamientoService/RubroGastoService.ListarActivas/os exigen VerFinanzas— y
    /// GuardarCommand queda permanentemente deshabilitado porque PuedeGuardar exige
    /// FuenteSeleccionada y RubroSeleccionado no nulos) y GestionarProductos. Este último NO es
    /// el mismo caso que /proveedores/activas (gateado a VerFinanzas, más laxo que
    /// GestionarMaestrosFinanzas): los endpoints GET /productos, /categorias/activas y
    /// /unidades-medida/activas que consume IngresoPorFacturaViewModel.InicializarAsync para
    /// poblar ProductosDisponibles/CategoriasDisponibles/UnidadesMedidaDisponibles no tienen
    /// ninguna ruta de lectura alternativa más laxa — exigen GestionarProductos sin excepción.
    /// ProductosDisponibles en particular es el ÚNICO combo para elegir un producto EXISTENTE en
    /// un renglón (no solo para el alta de producto nuevo, que sí es condicional del lado del
    /// servicio — ver IngresoPorFacturaService.RegistrarAsync, requierePermisoCatalogo): sin
    /// GestionarProductos la pantalla es inusable incluso en el caso base.
    /// </summary>
    public bool PuedeIngresarPorFactura =>
        _session.RolActual == RolUsuario.Admin ||
        (_session.PermisosActuales.Contains(Permisos.RegistrarMovimientos) &&
         _session.PermisosActuales.Contains(Permisos.RegistrarGastos) &&
         _session.PermisosActuales.Contains(Permisos.VerFinanzas) &&
         _session.PermisosActuales.Contains(Permisos.GestionarProductos));

    /// <summary>
    /// Registrar Entrada / Registrar Salida (fix bug de coherencia de permisos, 2026-08-16):
    /// a diferencia de PuedeRegistrarMovimientos (usado por Historial de movimientos), combina
    /// DOS permisos porque MovimientoRegistroViewModelBase.InicializarAsync (base común de
    /// EntradaRegistroViewModel/SalidaRegistroViewModel) carga IProductoService.BuscarAsync
    /// (GET /productos, ProductosEndpoints.cs) para poblar el combo de producto, y ese endpoint
    /// exige GestionarProductos sin ninguna ruta de lectura alternativa — mismo caso que
    /// ProductosDisponibles en PuedeIngresarPorFactura. El combo de MovimientoFormControl es el
    /// ÚNICO modo de elegir un producto existente en el renglón (no hay campo de código/SKU ni
    /// escaneo), así que sin GestionarProductos la pantalla queda inusable en el caso base, no
    /// solo para gestión de catálogo.
    /// </summary>
    public bool PuedeRegistrarEntradaSalida =>
        _session.RolActual == RolUsuario.Admin ||
        (_session.PermisosActuales.Contains(Permisos.RegistrarMovimientos) &&
         _session.PermisosActuales.Contains(Permisos.GestionarProductos));

    /// <summary>
    /// Historial por producto (fix bug de coherencia de permisos, 2026-08-16, auditoría):
    /// ReporteStockService.ObtenerHistorialPorProductoAsync verifica VerReportes pero DELEGA en
    /// MovimientoStockService.ObtenerHistorialAsync, que exige RegistrarMovimientos -- un permiso
    /// independiente. El comentario "DOBLE-GUARD" original asumía que VerReportes era Admin-only
    /// (premisa que dejó de ser cierta cuando pasó a ser configurable por usuario). Se endurece
    /// el gate de ENTRADA a esta pantalla en vez de relajar el servicio delegado: mismo criterio
    /// que PuedeRegistrarEntradaSalida/PuedeIngresarPorFactura -- el gate exige el MÁXIMO de los
    /// permisos que piden las capas de abajo, nunca el mínimo. A diferencia de las otras 4
    /// pantallas de Reportes (Valorización/StockCategoria/MasMovidos/AuditoriaLog), que solo
    /// verifican VerReportes sin delegar a otro servicio con otro permiso.
    /// </summary>
    public bool PuedeVerHistorialPorProducto =>
        _session.RolActual == RolUsuario.Admin ||
        (_session.PermisosActuales.Contains(Permisos.VerReportes) &&
         _session.PermisosActuales.Contains(Permisos.RegistrarMovimientos));

    /// <summary>
    /// Número de versión de la app para mostrar al pie del menú lateral (ej. "v0.1.1").
    /// </summary>
    public string VersionTexto => $"v{_infoApp.Version}";

    /// <summary>
    /// ViewModel activo en la región de contenido. Se actualiza cuando el INavigationService
    /// notifica un cambio via el evento Cambiado.
    /// </summary>
    [ObservableProperty]
    private ViewModelBase? _currentContent;

    /// <summary>
    /// Item fijo "Inicio", fuera de todo grupo (tabla de grupos, spec 2026-08-18: "(ninguno) —
    /// Inicio — siempre, queda fijo arriba").
    /// </summary>
    public ItemNavegacion ItemInicio { get; }

    /// <summary>
    /// Los 8 grupos colapsables del sidebar (Tanda 5). Títulos e íconos son literales copiados
    /// de ShellMainView.axaml — la única etiqueta nueva es "Movimientos" (ver spec, excepción
    /// explícita al Global Constraint de copy).
    /// </summary>
    public IReadOnlyList<GrupoNavegacion> Grupos { get; }

    /// <summary>
    /// Items creados vía <see cref="CrearItem"/> junto con el evaluador de su gate de permiso.
    /// Ruling 6 (2026-08-19): sin esto, EsVisible es un snapshot tomado una sola vez en el
    /// constructor -- <see cref="RecalcularVisibilidad"/> lo recorre para reevaluar cada item
    /// contra las propiedades Puede* actuales después de un refresco de permisos. ItemInicio no
    /// participa: su EsVisible es fijo (true), no depende de ningún permiso.
    /// </summary>
    private readonly List<(ItemNavegacion Item, Func<bool> Evaluador)> _itemsGateados = new();

    /// <summary>
    /// Crea un ItemNavegacion y lo registra en <see cref="_itemsGateados"/> junto con el
    /// evaluador de su gate, para que <see cref="RecalcularVisibilidad"/> pueda reasignarle
    /// EsVisible más adelante sin tener que repetir la tabla Seccion→permiso en otro lugar.
    /// </summary>
    private ItemNavegacion CrearItem(string titulo, string icono, ICommand comando, string seccion, Func<bool> evaluador)
    {
        var item = new ItemNavegacion(titulo, icono, comando, seccion, evaluador());
        _itemsGateados.Add((item, evaluador));
        return item;
    }

    public ShellMainViewModel(
        ICurrentSession session,
        INavigationService navigation,
        IInfoApp infoApp,
        IConfirmacionService confirmacion,
        IAuthService authService,
        IServicioPreferenciasSidebar preferencias)
    {
        _session      = session;
        _navigation   = navigation;
        _infoApp      = infoApp;
        _confirmacion = confirmacion;
        _authService  = authService;
        _preferencias = preferencias;

        // Suscribirse al evento del servicio para actualizar la región de contenido
        _navigation.Cambiado += OnNavegacionCambiada;

        ItemInicio = new ItemNavegacion("Inicio", "mdi-home", NavInicioCommand, "Inicio", true);

        Grupos = new List<GrupoNavegacion>
        {
            new GrupoNavegacion("Movimientos", new List<ItemNavegacion>
            {
                CrearItem("Productos", "mdi-package-variant", NavProductosCommand, "Productos", () => PuedeGestionarProductos),
                CrearItem("Registrar Entrada", "mdi-tray-arrow-down", NavRegistrarEntradaCommand, "RegistrarEntrada", () => PuedeRegistrarEntradaSalida),
                CrearItem("Ingreso por factura", "mdi-receipt-text-plus", NavIngresoPorFacturaCommand, "IngresoPorFactura", () => PuedeIngresarPorFactura),
                CrearItem("Registrar Salida", "mdi-tray-arrow-up", NavRegistrarSalidaCommand, "RegistrarSalida", () => PuedeRegistrarEntradaSalida),
                CrearItem("Historial de movimientos", "mdi-history", NavHistorialMovimientosCommand, "HistorialMovimientos", () => PuedeRegistrarMovimientos),
            }),
            new GrupoNavegacion("Tareas", new List<ItemNavegacion>
            {
                CrearItem("Tareas", "mdi-checkbox-marked-outline", NavTareasCommand, "Tareas", () => PuedeGestionarTareas),
            }),
            new GrupoNavegacion("Documentos", new List<ItemNavegacion>
            {
                CrearItem("Documentos administrativos", "mdi-file-cabinet", NavDocumentosCommand, "Documentos", () => PuedeGestionarDocumentos),
            }),
            new GrupoNavegacion("Finanzas", new List<ItemNavegacion>
            {
                CrearItem("Gastos y facturas", "mdi-receipt-text", NavGastosCommand, "Gastos", () => PuedeVerFinanzas),
                CrearItem("Ingresos de caja", "mdi-cash-plus", NavIngresosCommand, "Ingresos", () => PuedeVerFinanzas),
                CrearItem("Maestros de finanzas", "mdi-cash-multiple", NavMaestrosFinanzasCommand, "MaestrosFinanzas", () => PuedeGestionarMaestrosFinanzas),
                CrearItem("Libro caja", "mdi-book-open-variant", NavLibroCajaCommand, "LibroCaja", () => PuedeVerFinanzas),
                CrearItem("Control POA", "mdi-chart-donut", NavControlPoaCommand, "ControlPoa", () => PuedeVerFinanzas),
                CrearItem("Calendario de pagos", "mdi-calendar-clock", NavCalendarioPagosCommand, "CalendarioPagos", () => PuedeVerFinanzas),
            }),
            new GrupoNavegacion("Importación", new List<ItemNavegacion>
            {
                CrearItem("Importar planillas", "mdi-file-upload", NavImportacionCommand, "Importacion", () => EsAdmin),
            }),
            new GrupoNavegacion("Tablas maestras", new List<ItemNavegacion>
            {
                CrearItem("Categorías", "mdi-shape", NavCategoriasCommand, "Categorias", () => PuedeGestionarTablasMaestras),
                CrearItem("Proveedores", "mdi-truck", NavProveedoresCommand, "Proveedores", () => PuedeGestionarTablasMaestras),
                CrearItem("Unidades de medida", "mdi-ruler", NavUnidadesMedidaCommand, "UnidadesMedida", () => PuedeGestionarTablasMaestras),
            }),
            new GrupoNavegacion("Reportes", new List<ItemNavegacion>
            {
                CrearItem("Valorización de inventario", "mdi-currency-usd", NavValorizacionCommand, "Valorizacion", () => PuedeVerReportes),
                CrearItem("Stock por categoría", "mdi-chart-pie", NavStockCategoriaCommand, "StockCategoria", () => PuedeVerReportes),
                CrearItem("Historial por producto", "mdi-file-document", NavHistorialPorProductoCommand, "HistorialPorProducto", () => PuedeVerHistorialPorProducto),
                CrearItem("Productos más movidos", "mdi-trending-up", NavMasMovidosCommand, "MasMovidos", () => PuedeVerReportes),
                CrearItem("Log de auditoría", "mdi-shield-search", NavAuditoriaLogCommand, "AuditoriaLog", () => PuedeVerReportes),
            }),
            new GrupoNavegacion("Administración", new List<ItemNavegacion>
            {
                CrearItem("Mantenimiento", "mdi-database-cog", NavMantenimientoCommand, "Mantenimiento", () => EsAdmin),
                CrearItem("Usuarios", "mdi-account-cog", NavUsuariosCommand, "Usuarios", () => EsAdmin),
            }),
        };

        // Restaurar expansión guardada (Task 5.1). Un nombre de grupo desconocido (ej. si se
        // renombró un grupo entre versiones) se ignora sin romper el arranque.
        var gruposAbiertos = _preferencias.Cargar()?.GruposAbiertos ?? Array.Empty<string>();
        foreach (var grupo in Grupos)
            grupo.EstaExpandido = gruposAbiertos.Contains(grupo.Titulo);
    }

    /// <summary>
    /// Alterna la expansión de un grupo del sidebar y persiste la preferencia (Task 5.1).
    /// </summary>
    [RelayCommand]
    private void AlternarGrupo(GrupoNavegacion grupo)
    {
        grupo.EstaExpandido = !grupo.EstaExpandido;

        var abiertos = Grupos.Where(g => g.EstaExpandido).Select(g => g.Titulo).ToList();
        _preferencias.Guardar(new PreferenciasSidebar(abiertos));
    }

    /// <summary>
    /// Handler de <see cref="INavigationService.Cambiado"/>. Se guarda como método (y no como
    /// lambda inline) para poder desuscribirlo en <see cref="Desconectar"/>: INavigationService
    /// es Singleton y ShellMainViewModel es Transient, así que cada ciclo login→logout→login
    /// crea una instancia nueva; sin desuscripción, el delegate del singleton retiene para
    /// siempre cada instancia vieja (leak lineal) y dispara notificaciones redundantes en
    /// cada Navegar&lt;T&gt;() sobre VMs que ya no están en pantalla.
    /// </summary>
    private void OnNavegacionCambiada()
    {
        // El contenido del shell nunca puede ser el propio shell: evita la recursión
        // ShellMainView dentro de ShellMainView (StackOverflow al renderizar).
        // Se compara por referencia contra 'this' (no con el tipo) porque es la guardia
        // más estricta posible: excluye exactamente la instancia que causaría el ciclo,
        // sin descartar por error una futura subclase de ShellMainViewModel que sí
        // debiera poder navegarse como contenido válido.
        if (!ReferenceEquals(_navigation.Actual, this))
            CurrentContent = _navigation.Actual;

        // Refresco de permisos al navegar (spec decisión 7): best-effort, en segundo plano —
        // si el Admin revocó un permiso mientras la sesión seguía abierta, el menú se actualiza
        // sin esperar a la próxima acción que dispare un 403 (Task 15). No bloquea la
        // navegación: si la API está caída, el usuario sigue navegando con el cache viejo.
        _tareaRefrescoPermisos = RefrescarPermisosAsync();
    }

    /// <summary>
    /// Espera el refresco best-effort y luego notifica las 7 propiedades Puede* (pre-flight,
    /// mismo riesgo que el bug crítico de Task 13 con los checkboxes del panel de permisos):
    /// son getters calculados sobre ICurrentSession.PermisosActuales, que no implementa
    /// INotifyPropertyChanged, así que nadie avisa a los bindings del menú cuando el cache
    /// local cambia por debajo. Sin este aviso manual, el menú quedaría "congelado" con los
    /// permisos viejos hasta la próxima navegación (o para siempre, en el caso de Inicio).
    /// </summary>
    private async Task RefrescarPermisosAsync()
    {
        await RefrescoPermisos.DispararBestEffortAsync(
            () => _authService.ObtenerPermisosPropiosAsync(), nameof(ShellMainViewModel));

        OnPropertyChanged(nameof(PuedeGestionarProductos));
        OnPropertyChanged(nameof(PuedeRegistrarMovimientos));
        OnPropertyChanged(nameof(PuedeGestionarTareas));
        OnPropertyChanged(nameof(PuedeGestionarDocumentos));
        OnPropertyChanged(nameof(PuedeVerFinanzas));
        OnPropertyChanged(nameof(PuedeGestionarMaestrosFinanzas));
        OnPropertyChanged(nameof(PuedeGestionarTablasMaestras));
        OnPropertyChanged(nameof(PuedeVerReportes));
        OnPropertyChanged(nameof(PuedeIngresarPorFactura));
        OnPropertyChanged(nameof(PuedeRegistrarEntradaSalida));
        OnPropertyChanged(nameof(PuedeVerHistorialPorProducto));
        RecalcularVisibilidad();
    }

    /// <summary>
    /// Ruling 6 (2026-08-19): reevalúa el EsVisible de cada ItemNavegacion gateado contra las
    /// propiedades Puede* actuales y propaga el cambio a los grupos que derivan su visibilidad de
    /// sus items. Sin este método, EsVisible quedaba fijo en el valor tomado por CrearItem en el
    /// constructor: las notificaciones de las Puede* de arriba refrescan los bindings que leen
    /// esas propiedades directamente (ej. un futuro panel de permisos), pero el sidebar de la
    /// Task 5.3 bindea IsVisible al ItemNavegacion, no a las Puede* -- a un usuario al que se le
    /// revoca un permiso en caliente, el botón le hubiera quedado visible.
    /// </summary>
    private void RecalcularVisibilidad()
    {
        foreach (var (item, evaluador) in _itemsGateados)
            item.EsVisible = evaluador();

        foreach (var grupo in Grupos)
            grupo.ActualizarVisibilidad();
    }

    /// <summary>
    /// Desconecta esta instancia de los eventos externos (singletons) a los que se suscribió
    /// en el constructor. Debe invocarse desde la composition root (ShellViewModel) antes de
    /// reemplazar el VM actual al cerrar sesión, para que el <see cref="INavigationService"/>
    /// (Singleton) no retenga esta instancia indefinidamente vía su delegate de
    /// <see cref="INavigationService.Cambiado"/>. Idempotente: desuscribir dos veces el mismo
    /// handler no falla ni tiene efecto extra.
    /// </summary>
    public void Desconectar()
    {
        _navigation.Cambiado -= OnNavegacionCambiada;
    }

    /// <summary>
    /// Nombre lógico de la sección actualmente activa en el sidebar (ej. "Productos").
    /// Se usa desde ShellMainView.axaml para resaltar el ítem de navegación seleccionado
    /// (Classes.active + ObjectConverters.Equal). Null hasta la primera navegación.
    /// </summary>
    [ObservableProperty]
    private string? _seccionActiva;

    /// <summary>
    /// Setter parcial que CommunityToolkit genera para <see cref="SeccionActiva"/>: una sola
    /// implementación en vez de repetir la lógica en cada uno de los 24 NavXxx(). Actualiza
    /// <see cref="ItemNavegacion.EstaActivo"/> de todos los items (Ruling 2026-08-19: Avalonia no
    /// acepta {Binding} en ConverterParameter, así que el resaltado del item activo ya no puede
    /// resolverse en el XAML con un converter) y auto-expande el grupo que contiene la sección
    /// recién activada (spec: "el grupo que contiene la sección activa se abre solo"), sin cerrar
    /// los demás grupos ya abiertos.
    /// </summary>
    partial void OnSeccionActivaChanged(string? value)
    {
        ItemInicio.EstaActivo = ItemInicio.Seccion == value;

        foreach (var grupo in Grupos)
        {
            var contieneActiva = false;

            foreach (var item in grupo.Items)
            {
                item.EstaActivo = item.Seccion == value;
                if (item.EstaActivo) contieneActiva = true;
            }

            if (contieneActiva)
                grupo.EstaExpandido = true;
        }
    }

    // ── comandos de navegación ────────────────────────────────────────────────

    [RelayCommand]
    private void NavInicio()
    {
        SeccionActiva = "Inicio";
        _navigation.Navegar<InicioViewModel>();
    }

    [RelayCommand]
    private void NavProductos()
    {
        SeccionActiva = "Productos";
        _navigation.Navegar<ProductoListViewModel>();
    }

    [RelayCommand]
    private void NavCategorias()
    {
        SeccionActiva = "Categorias";
        _navigation.Navegar<CategoriaListViewModel>();
    }

    [RelayCommand]
    private void NavProveedores()
    {
        SeccionActiva = "Proveedores";
        _navigation.Navegar<ProveedorListViewModel>();
    }

    [RelayCommand]
    private void NavUnidadesMedida()
    {
        SeccionActiva = "UnidadesMedida";
        _navigation.Navegar<UnidadMedidaListViewModel>();
    }

    // ── Movimientos (Inc 5): Operador + Admin ─────────────────────────────────

    [RelayCommand]
    private void NavRegistrarEntrada()
    {
        SeccionActiva = "RegistrarEntrada";
        _navigation.Navegar<EntradaRegistroViewModel>();
    }

    [RelayCommand]
    private void NavRegistrarSalida()
    {
        SeccionActiva = "RegistrarSalida";
        _navigation.Navegar<SalidaRegistroViewModel>();
    }

    [RelayCommand]
    private void NavIngresoPorFactura()
    {
        SeccionActiva = "IngresoPorFactura";
        _navigation.Navegar<IngresoPorFacturaViewModel>();
    }

    [RelayCommand]
    private void NavHistorialMovimientos()
    {
        SeccionActiva = "HistorialMovimientos";
        _navigation.Navegar<MovimientoHistorialViewModel>();
    }

    // ── Tareas (spec 2026-08-01): Admin y Operador ────────────────────────────

    [RelayCommand]
    private void NavTareas()
    {
        SeccionActiva = "Tareas";
        _navigation.Navegar<TareaListViewModel>();
    }

    // ── Documentos administrativos (spec 2026-08-11): Admin y Operador con permiso ───────────

    [RelayCommand]
    private void NavDocumentos()
    {
        SeccionActiva = "Documentos";
        _navigation.Navegar<DocumentoListViewModel>();
    }

    // ── Reportes (Inc 6): solo Admin ──────────────────────────────────────────

    [RelayCommand]
    private void NavValorizacion()
    {
        SeccionActiva = "Valorizacion";
        _navigation.Navegar<ValorizacionViewModel>();
    }

    [RelayCommand]
    private void NavStockCategoria()
    {
        SeccionActiva = "StockCategoria";
        _navigation.Navegar<StockCategoriaViewModel>();
    }

    [RelayCommand]
    private void NavHistorialPorProducto()
    {
        SeccionActiva = "HistorialPorProducto";
        _navigation.Navegar<HistorialPorProductoViewModel>();
    }

    [RelayCommand]
    private void NavMasMovidos()
    {
        SeccionActiva = "MasMovidos";
        _navigation.Navegar<MasMovidosViewModel>();
    }

    [RelayCommand]
    private void NavAuditoriaLog()
    {
        SeccionActiva = "AuditoriaLog";
        _navigation.Navegar<AuditoriaLogViewModel>();
    }

    // ── Administracion (Entrega 1 Backups): solo Admin ────────────────────────

    [RelayCommand]
    private void NavMantenimiento()
    {
        SeccionActiva = "Mantenimiento";
        _navigation.Navegar<MantenimientoViewModel>();
    }

    /// <summary>ABM de usuarios (Task 12, spec 2026-08-10): antes solo se administraba por
    /// curl contra la API; protegido por GestionarUsuarios, uno de los 4 permisos
    /// estructurales Admin-only siempre — mismo gating (EsAdmin) que Mantenimiento hasta que
    /// la Task 14 introduzca el gating genérico por permiso.</summary>
    [RelayCommand]
    private void NavUsuarios()
    {
        SeccionActiva = "Usuarios";
        _navigation.Navegar<UsuariosAdminViewModel>();
    }

    // ── Finanzas — Fase 1: Admin y Operador ───────────────────────────────────

    [RelayCommand]
    private void NavGastos()
    {
        SeccionActiva = "Gastos";
        _navigation.Navegar<GastosViewModel>();
    }

    [RelayCommand]
    private void NavIngresos()
    {
        SeccionActiva = "Ingresos";
        _navigation.Navegar<IngresosViewModel>();
    }

    [RelayCommand]
    private void NavMaestrosFinanzas()
    {
        SeccionActiva = "MaestrosFinanzas";
        _navigation.Navegar<MaestrosFinanzasViewModel>();
    }

    [RelayCommand]
    private void NavLibroCaja()
    {
        SeccionActiva = "LibroCaja";
        _navigation.Navegar<LibroCajaViewModel>();
    }

    [RelayCommand]
    private void NavControlPoa()
    {
        SeccionActiva = "ControlPoa";
        _navigation.Navegar<ControlPoaViewModel>();
    }

    [RelayCommand]
    private void NavCalendarioPagos()
    {
        SeccionActiva = "CalendarioPagos";
        _navigation.Navegar<CalendarioPagosViewModel>();
    }

    [RelayCommand]
    private void NavImportacion()
    {
        SeccionActiva = "Importacion";
        _navigation.Navegar<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>();
    }

    // ── Cerrar sesión ──────────────────────────────────────────────────────────

    /// <summary>
    /// Pide confirmación y, si el usuario acepta, limpia la sesión actual (token JWT +
    /// snapshot de identidad, vía ICurrentSession.CerrarSesion()) y dispara
    /// <see cref="CerrarSesionSolicitado"/> para que la composition root navegue al login.
    /// </summary>
    [RelayCommand]
    private async Task CerrarSesionAsync()
    {
        var confirmar = await _confirmacion.PreguntarAsync("¿Cerrar la sesión?");
        if (!confirmar) return;

        _session.CerrarSesion();
        CerrarSesionSolicitado?.Invoke();
    }
}
