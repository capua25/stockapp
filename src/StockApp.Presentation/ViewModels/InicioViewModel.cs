using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Converters;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Pantalla de bienvenida mostrada en la región central del shell tras el login.
/// Resuelve el bug de "región central vacía tras login": es el primer contenido
/// navegado dentro de ShellMainViewModel una vez que este queda establecido como
/// CurrentViewModel del shell.
/// </summary>
public partial class InicioViewModel : ViewModelBase
{
    private readonly ICurrentSession        _session;
    private readonly INavigationService     _navigation;
    private readonly IFinanzasVistasService _finanzasVistas;
    private readonly IBackupsService        _backups;
    private readonly ITareaService          _tareas;
    private readonly IAuthService           _authService;

    /// <summary>
    /// Task del refresco de permisos disparado por <see cref="CargarAsync"/> (review Ronda 1,
    /// Task 14): internal (+ InternalsVisibleTo) para que los tests lo esperen de forma
    /// determinista, mismo patrón que ShellMainViewModel._tareaRefrescoPermisos y
    /// PanelPermisosViewModel._tareaCarga (Task 13).
    /// </summary>
    internal Task _tareaRefrescoPermisos = Task.CompletedTask;

    public string NombreUsuario =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Usuario";

    public string Saludo => $"¡Bienvenido, {NombreUsuario}!";

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public string RolTexto => EsAdmin ? "Administrador" : "Operador";

    public bool PuedeVerReportes =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerReportes);

    /// <summary>
    /// Gates de los accesos rápidos operativos (fix bug de coherencia de permisos, 2026-08-16):
    /// auditoría de permisos encontró que Productos/Registrar Entrada/Registrar Salida/Historial
    /// de movimientos NO tenían gate acá -- InicioViewModel nunca replicó las propiedades Puede*
    /// que sí tiene ShellMainViewModel (sidebar) para esas mismas 4 pantallas, a pesar de que el
    /// patrón ya se usaba en este mismo archivo/vista para Valorización/Auditoría
    /// (PuedeVerReportes). Un Operador sin GestionarProductos veía las 4 tarjetas, clickeaba
    /// "Productos" y se comía un 403 en silencio (ProductoListViewModel.CargarAsync no atrapaba
    /// UnauthorizedAccessException, dejando la grilla vacía para siempre sin explicación). Mismas
    /// condiciones EXACTAS que ShellMainViewModel para mantener la coherencia sidebar/Inicio.
    /// </summary>
    public bool PuedeGestionarProductos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarProductos);

    public bool PuedeRegistrarMovimientos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.RegistrarMovimientos);

    /// <summary>
    /// Registrar Entrada / Registrar Salida: combina DOS permisos, mismo criterio y mismo
    /// comentario que ShellMainViewModel.PuedeRegistrarEntradaSalida -- el combo de producto de
    /// EntradaRegistroViewModel/SalidaRegistroViewModel (vía MovimientoRegistroViewModelBase)
    /// exige GestionarProductos sin ninguna ruta de lectura alternativa, así que
    /// RegistrarMovimientos solo no alcanza.
    /// </summary>
    public bool PuedeRegistrarEntradaSalida =>
        _session.RolActual == RolUsuario.Admin ||
        (_session.PermisosActuales.Contains(Permisos.RegistrarMovimientos) &&
         _session.PermisosActuales.Contains(Permisos.GestionarProductos));

    /// <summary>
    /// Gate del panel "Accesos rápidos" completo (bug cosmético, 2026-08-17): el Border que
    /// envuelve las 6 tarjetas no tenía IsVisible propio -- si un usuario no cumplía NINGUNO de
    /// los 6 gates de las tarjetas (ej. opfinanzas, que solo tiene VerFinanzas), las 6 tarjetas se
    /// ocultaban pero el card y el título "Accesos rápidos" quedaban visibles, vacíos. Se deriva
    /// de las propiedades Puede* existentes -- nunca re-consulta permisos ni duplica lógica --
    /// para que no se pueda desincronizar de las tarjetas: si el día de mañana se agrega o saca
    /// una tarjeta, este gate cambia solo junto con ella.
    /// </summary>
    public bool PuedeVerAccesosRapidos =>
        PuedeGestionarProductos || PuedeRegistrarEntradaSalida || PuedeRegistrarMovimientos || PuedeVerReportes;

    /// <summary>
    /// Bug 2026-08-15: CargarAsync llamaba a GET /finanzas/calendario-pagos (que exige
    /// Permisos.VerFinanzas, FinanzasVistasEndpoints) sin importar si el usuario tenía el
    /// permiso -- el 403 resultante quedaba tragado por el catch genérico, dejando el aviso de
    /// vencimientos sin mostrarse y sin que nadie se entere (falla silenciosa). Mismo patrón que
    /// PuedeVerReportes: se chequea ANTES de llamar, nunca después de que la llamada ya falló.
    /// </summary>
    public bool PuedeVerCalendarioPagos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerFinanzas);

    /// <summary>
    /// Mismo bug y mismo criterio que PuedeVerCalendarioPagos: GET /tareas exige
    /// Permisos.GestionarTareas (TareasEndpoints) -- un Operador sin ese permiso no debe ni
    /// intentar la llamada.
    /// </summary>
    public bool PuedeVerTareas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTareas);

    [ObservableProperty] private bool _mostrarAvisoVencimientos;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoVencidas))]
    private int _cantidadVencidas;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoAVencer7Dias))]
    private int _cantidadAVencer7Dias;

    public string TextoVencidas =>
        CantidadVencidas == 1 ? "1 factura vencida" : $"{CantidadVencidas} facturas vencidas";

    public string TextoAVencer7Dias =>
        CantidadAVencer7Dias == 1
            ? "1 factura por vencer esta semana"
            : $"{CantidadAVencer7Dias} facturas por vencer esta semana";

    // Tercer estado (review final E1): un fallo consultando /backups/salud (API caída, 403,
    // 404 por una versión vieja del servidor sin la ruta, cambio de forma del JSON) NO es lo
    // mismo que "backup al día" ni que "backup vencido" — antes el catch de más abajo ocultaba
    // el aviso, maquillando un fallo de consulta como salud OK, la inversión exacta del
    // principio de esta entrega. MostrarAvisoBackup sigue siendo "hay que avisar algo" (true
    // para Problema Y Desconocido); AvisoBackupEsDesconocido discrimina cuál de los dos, para
    // que la vista pueda mostrar textos y colores distintos sin afirmar ninguno de los otros
    // dos estados.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupProblema))]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupDesconocido))]
    private bool _mostrarAvisoBackup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupProblema))]
    [NotifyPropertyChangedFor(nameof(MostrarAvisoBackupDesconocido))]
    private bool _avisoBackupEsDesconocido;

    [ObservableProperty] private string? _textoAvisoBackup;

    public bool MostrarAvisoBackupProblema => MostrarAvisoBackup && !AvisoBackupEsDesconocido;

    public bool MostrarAvisoBackupDesconocido => MostrarAvisoBackup && AvisoBackupEsDesconocido;

    // ── Panel "Tareas que requieren atención" (spec 2026-08-06) ────────────────

    public ObservableCollection<TareaFila> TareasVencidas { get; } = new();
    public ObservableCollection<TareaFila> TareasProximasAVencer { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarPanelTareas))]
    [NotifyPropertyChangedFor(nameof(MostrarSeccionTareasVencidas))]
    [NotifyPropertyChangedFor(nameof(TituloVencidas))]
    private int _cantidadTareasVencidas;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MostrarPanelTareas))]
    [NotifyPropertyChangedFor(nameof(MostrarSeccionTareasProximasAVencer))]
    [NotifyPropertyChangedFor(nameof(TituloProximasAVencer))]
    private int _cantidadTareasProximasAVencer;

    /// <summary>
    /// Decisión del encargo: si no hay ninguna tarea vencida ni próxima a vencer, el panel
    /// entero no se muestra -- nunca un cartel vacío ensuciando la pantalla de Inicio.
    /// </summary>
    public bool MostrarPanelTareas => CantidadTareasVencidas > 0 || CantidadTareasProximasAVencer > 0;

    public bool MostrarSeccionTareasVencidas => CantidadTareasVencidas > 0;
    public bool MostrarSeccionTareasProximasAVencer => CantidadTareasProximasAVencer > 0;

    public string TituloVencidas => $"VENCIDAS ({CantidadTareasVencidas})";
    public string TituloProximasAVencer => $"PRÓXIMAS A VENCER ({CantidadTareasProximasAVencer})";

    public InicioViewModel(
        ICurrentSession session, INavigationService navigation,
        IFinanzasVistasService finanzasVistas, IBackupsService backups, ITareaService tareas,
        IAuthService authService)
    {
        _session        = session;
        _navigation     = navigation;
        _finanzasVistas = finanzasVistas;
        _backups        = backups;
        _tareas         = tareas;
        _authService    = authService;
    }

    /// <summary>
    /// Carga el aviso de vencimientos (spec §7.5: "al abrir la app, aviso en Inicio si hay
    /// facturas vencidas o por vencer en la semana"). Sin VerFinanzas o si la API falla, el
    /// aviso simplemente no se muestra — Inicio nunca debe romper (catch silencioso).
    /// </summary>
    public async Task CargarAsync()
    {
        // Refresco de permisos al entrar a Inicio (review Ronda 1, Task 14): InicioView.axaml.cs
        // llama a CargarAsync() en cada DataContextChanged, es decir, en CADA navegación (el VM
        // es Transient). Sin este refresco propio, un Operador al que se le concede VerReportes
        // con la sesión abierta no ve los accesos rápidos hasta la SEGUNDA vez que entra a
        // Inicio: el sidebar SÍ se actualiza al toque (ShellMainViewModel notifica desde una
        // instancia que persiste), pero esta instancia nueva se construyó y evaluó
        // PuedeVerReportes ANTES de que termine cualquier refresco disparado por la navegación
        // anterior. Fire-and-forget, igual que ShellMainViewModel.OnNavegacionCambiada: no
        // bloquea la carga del resto de la pantalla ni introduce una espera visible.
        _tareaRefrescoPermisos = RefrescarPermisosAsync();

        // Bug 2026-08-15: antes se llamaba siempre, sin importar si el usuario tenía
        // Permisos.VerFinanzas -- un Operador sin ese permiso se comía un 403 tragado por el
        // catch de abajo. Ahora Inicio no pide lo que sabe que no puede pedir: si no tiene el
        // permiso, el widget simplemente no se muestra, sin siquiera intentar la llamada.
        if (PuedeVerCalendarioPagos)
        {
            try
            {
                var calendario = await _finanzasVistas.ObtenerCalendarioPagosAsync();
                CantidadVencidas = calendario.Vencidas.Count;
                CantidadAVencer7Dias = calendario.AVencer7Dias.Count;
                MostrarAvisoVencimientos = CantidadVencidas > 0 || CantidadAVencer7Dias > 0;
            }
            catch (Exception)
            {
                MostrarAvisoVencimientos = false;
            }
        }
        else
        {
            MostrarAvisoVencimientos = false;
        }

        // Panel "Tareas que requieren atención" (spec 2026-08-06): try/catch propio, igual
        // que el aviso de arriba -- un fallo consultando /tareas (API caída) no debe afectar a
        // ningún otro aviso de esta pantalla, y nunca debe romper Inicio. Bug 2026-08-15: la
        // llamada ahora está gateada por PuedeVerTareas (Permisos.GestionarTareas) por el mismo
        // motivo que el calendario de arriba -- sin permiso, ni se intenta.
        if (PuedeVerTareas)
        {
            try
            {
                var tareas = await _tareas.ListarAsync();
                var rol = _session.RolActual ?? RolUsuario.Operador;
                var usuarioActualId = _session.UsuarioActual?.Id ?? 0;

                var (vencidas, proximas) = PanelVencimientosTareas.Agrupar(tareas, rol, usuarioActualId);

                TareasVencidas.Clear();
                foreach (var fila in vencidas) TareasVencidas.Add(fila);

                TareasProximasAVencer.Clear();
                foreach (var fila in proximas) TareasProximasAVencer.Add(fila);

                CantidadTareasVencidas = TareasVencidas.Count;
                CantidadTareasProximasAVencer = TareasProximasAVencer.Count;
            }
            catch (Exception)
            {
                TareasVencidas.Clear();
                TareasProximasAVencer.Clear();
                CantidadTareasVencidas = 0;
                CantidadTareasProximasAVencer = 0;
            }
        }
        else
        {
            TareasVencidas.Clear();
            TareasProximasAVencer.Clear();
            CantidadTareasVencidas = 0;
            CantidadTareasProximasAVencer = 0;
        }

        if (!EsAdmin)
        {
            MostrarAvisoBackup = false;
            return;
        }

        try
        {
            var salud = await _backups.ObtenerSaludAsync();
            MostrarAvisoBackup = salud.Vencido;
            AvisoBackupEsDesconocido = false;
            // UmbralHoras viaja en el DTO (SaludBackupDto, Task 6) — NUNCA hardcodear el número
            // acá: si el umbral cambia en ServicioConsultaBackups, este texto tiene que reflejarlo
            // solo, sin quedar mintiendo en silencio (pre-flight scan, corregido).
            if (salud.UltimoExitoEn is DateTime ultimo)
            {
                // Hora LOCAL, no UTC cruda: mismo patrón que MantenimientoView.axaml
                // (FechaUtcALocalConverter) — un backup mostraba distinta hora según la pantalla.
                var local = (DateTime)FechaUtcALocalConverter.Instance.Convert(
                    ultimo, typeof(DateTime), null, CultureInfo.InvariantCulture)!;
                TextoAvisoBackup =
                    $"El último backup exitoso fue el {local:dd/MM/yyyy HH:mm} (hace más de {salud.UmbralHoras} horas).";
            }
            else
            {
                TextoAvisoBackup = "Todavía no se registró ningún backup exitoso.";
            }
        }
        catch (Exception)
        {
            // Tercer estado: NO se pudo verificar (ver comentario de AvisoBackupEsDesconocido
            // más arriba) — se avisa igual que un problema real, pero sin afirmar que el backup
            // está vencido ni que está al día.
            MostrarAvisoBackup = true;
            AvisoBackupEsDesconocido = true;
            TextoAvisoBackup = "No se pudo verificar el estado del backup.";
        }
    }

    /// <summary>
    /// Espera el refresco best-effort y notifica <see cref="PuedeVerReportes"/> (mismo criterio
    /// que ShellMainViewModel.RefrescarPermisosAsync): es un getter calculado sobre
    /// ICurrentSession.PermisosActuales, que no implementa INotifyPropertyChanged, así que nadie
    /// avisa a los bindings de los accesos rápidos cuando el cache local cambia por debajo.
    /// </summary>
    private async Task RefrescarPermisosAsync()
    {
        await RefrescoPermisos.DispararBestEffortAsync(
            () => _authService.ObtenerPermisosPropiosAsync(), nameof(InicioViewModel));

        OnPropertyChanged(nameof(PuedeVerReportes));
        OnPropertyChanged(nameof(PuedeGestionarProductos));
        OnPropertyChanged(nameof(PuedeRegistrarMovimientos));
        OnPropertyChanged(nameof(PuedeRegistrarEntradaSalida));
        OnPropertyChanged(nameof(PuedeVerAccesosRapidos));
    }

    // ── accesos rápidos: comunes (Admin + Operador) ───────────────────────────

    [RelayCommand]
    private void IrAProductos() => _navigation.Navegar<ProductoListViewModel>();

    [RelayCommand]
    private void IrARegistrarEntrada() => _navigation.Navegar<EntradaRegistroViewModel>();

    [RelayCommand]
    private void IrARegistrarSalida() => _navigation.Navegar<SalidaRegistroViewModel>();

    [RelayCommand]
    private void IrAHistorialMovimientos() => _navigation.Navegar<MovimientoHistorialViewModel>();

    [RelayCommand]
    private void IrACalendarioPagos() => _navigation.Navegar<CalendarioPagosViewModel>();

    // ── panel de vencimientos de tareas ─────────────────────────────────────

    [RelayCommand]
    private void VerTarea(TareaFila fila) => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaVer(fila.Tarea));

    [RelayCommand]
    private void VerTodasLasTareas() => _navigation.Navegar<TareaListViewModel>();

    // ── accesos rápidos: solo Admin ────────────────────────────────────────────

    [RelayCommand]
    private void IrAValorizacion() => _navigation.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void IrAAuditoria() => _navigation.Navegar<AuditoriaLogViewModel>();
}
