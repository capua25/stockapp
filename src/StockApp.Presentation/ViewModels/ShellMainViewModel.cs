using System;
using System.Threading.Tasks;
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
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// ViewModel del shell principal post-login. Hostea el menú lateral y la región de contenido.
/// Los ítems de "Tablas maestras" solo son visibles para Admin; "Productos" está disponible
/// para Admin y Operador.
/// </summary>
public partial class ShellMainViewModel : ViewModelBase
{
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;
    private readonly IInfoApp           _infoApp;
    private readonly IConfirmacionService _confirmacion;
    private readonly IAuthService       _authService;

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

    public bool PuedeVerFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerFinanzas);

    public bool PuedeGestionarMaestrosFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarMaestrosFinanzas);

    public bool PuedeGestionarTablasMaestras =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTablasMaestras);

    public bool PuedeVerReportes =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerReportes);

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

    public ShellMainViewModel(
        ICurrentSession session,
        INavigationService navigation,
        IInfoApp infoApp,
        IConfirmacionService confirmacion,
        IAuthService authService)
    {
        _session      = session;
        _navigation   = navigation;
        _infoApp      = infoApp;
        _confirmacion = confirmacion;
        _authService  = authService;

        // Suscribirse al evento del servicio para actualizar la región de contenido
        _navigation.Cambiado += OnNavegacionCambiada;
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
        OnPropertyChanged(nameof(PuedeVerFinanzas));
        OnPropertyChanged(nameof(PuedeGestionarMaestrosFinanzas));
        OnPropertyChanged(nameof(PuedeGestionarTablasMaestras));
        OnPropertyChanged(nameof(PuedeVerReportes));
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
