using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Auth;
using StockApp.Application.Licenciamiento;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Shell de navegación. Decide qué pantalla mostrar en función del estado de la app:
/// licencia (Inc 7 Fase B) → login → contenido principal (ShellMainViewModel con menú lateral).
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly IAuthService            _authService;
    private readonly ILicenciaService        _licenciaService;
    private readonly IResetAdminService      _resetAdminService;
    private readonly INavigationService      _navigation;
    private readonly CoordinadorActualizacion _coordinadorActualizacion;
    private readonly IUiDispatcher           _uiDispatcher;
    private readonly IInfoApp                _infoApp;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    /// <summary>
    /// ViewModel del overlay de actualización activo (banner, modal o bloqueo),
    /// o null cuando no hay actualización pendiente. Se asigna en el UI thread
    /// tras completar la evaluación en background.
    /// </summary>
    [ObservableProperty]
    private ViewModelBase? _overlayActualizacion;

    public ShellViewModel(
        IAuthService             authService,
        ILicenciaService         licenciaService,
        IResetAdminService       resetAdminService,
        INavigationService       navigation,
        CoordinadorActualizacion coordinadorActualizacion,
        IUiDispatcher            uiDispatcher,
        IInfoApp                 infoApp)
    {
        _authService              = authService;
        _licenciaService          = licenciaService;
        _resetAdminService        = resetAdminService;
        _navigation               = navigation;
        _coordinadorActualizacion = coordinadorActualizacion;
        _uiDispatcher             = uiDispatcher;
        _infoApp                  = infoApp;
    }

    /// <summary>
    /// Debe llamarse una sola vez al arrancar la app. Decide el primer VM a mostrar:
    /// Inc 7 Fase B consulta la licencia primero — sin licencia activa muestra la pantalla
    /// de bloqueo, con licencia (o si la API está caída) va al login. Dispara el chequeo
    /// de actualizaciones en background sin bloquear el arranque.
    /// </summary>
    public async Task InicializarAsync()
    {
        try
        {
            var estado = await _licenciaService.ObtenerEstadoAsync();
            if (!estado.Activada)
                MostrarBloqueoLicencia();
            else
                MostrarLogin();
        }
        catch (Exception)
        {
            // API inalcanzable: no bloqueamos el arranque; el login muestra el error de conexión.
            MostrarLogin();
        }

        // Fire-and-forget controlado: el coordinador no debe tumbar el arranque si falla.
        // _tareaActualizacion se expone como internal para que los tests puedan awaitarla
        // y evitar condiciones de carrera con Task.Delay.
        _tareaActualizacion = EvaluarYAsignarOverlayAsync();
        _ = _tareaActualizacion;
    }

    /// <summary>
    /// Tarea del background de actualización. Expuesta como internal para poder
    /// awaitarla en tests y evitar condiciones de carrera con delays arbitrarios.
    /// </summary>
    internal Task _tareaActualizacion = Task.CompletedTask;

    /// <summary>
    /// Evalúa el coordinador en background y asigna el overlay en el UI thread al terminar.
    /// </summary>
    private async Task EvaluarYAsignarOverlayAsync()
    {
        await Task.Run(async () =>
        {
            try
            {
                await _coordinadorActualizacion.EvaluarEnArranqueAsync();
            }
            catch (Exception)
            {
                // Silencio: no interrumpir la app si el chequeo de updates falla.
                return;
            }
        });

        // Resolvemos el overlay (todavía en el thread del pool) y marshaleamos la asignación
        // al UI thread vía IUiDispatcher: OverlayActualizacion tiene binding activo en
        // MainWindow.axaml, y asignarla desde un hilo del thread-pool dispara PropertyChanged
        // fuera del UI thread, lo que puede crashear. En tests, IUiDispatcher se reemplaza por
        // un fake que ejecuta inline (sin depender de Dispatcher.UIThread, no inicializado ahí).
        var overlay = CoordinadorActualizacion.ResolverOverlayViewModel(
            _coordinadorActualizacion.AccionUxActual);

        SuscribirEventosOverlay(overlay);

        _uiDispatcher.Post(() => OverlayActualizacion = overlay);
    }

    /// <summary>
    /// Conecta los eventos de "aplicar actualización" y "posponer" del overlay resuelto al
    /// flujo real: aplicar dispara <see cref="CoordinadorActualizacion.AplicarActualizacionAsync"/>
    /// (descarga + reinicio); posponer cierra el overlay. Cada VM de overlay expone eventos con
    /// nombres propios (banner/modal/bloqueo), por eso se resuelve por tipo concreto.
    /// </summary>
    private void SuscribirEventosOverlay(ViewModelBase? overlay)
    {
        switch (overlay)
        {
            case ActualizacionBannerViewModel banner:
                banner.ActualizarAlReiniciarSolicitado += () => DispararAplicarActualizacion(banner);
                banner.PosponerSolicitado += CerrarOverlayActualizacion;
                break;

            case ActualizacionModalViewModel modal:
                modal.ActualizarAhoraSolicitado += () => DispararAplicarActualizacion(null);
                modal.PosponerSolicitado += CerrarOverlayActualizacion;
                break;

            case ActualizacionBloqueoViewModel bloqueo:
                bloqueo.AplicarYReiniciarSolicitado += () => DispararAplicarActualizacion(null);
                break;
        }
    }

    /// <summary>Cierra el overlay de actualización (usado por "Más tarde"/posponer).</summary>
    private void CerrarOverlayActualizacion()
        => _uiDispatcher.Post(() => OverlayActualizacion = null);

    /// <summary>
    /// Tarea del background disparada por el último "aplicar actualización" (banner/modal/
    /// bloqueo). Expuesta como internal para que los tests puedan awaitarla de forma
    /// determinista, igual que <see cref="_tareaActualizacion"/>.
    /// </summary>
    internal Task _tareaAplicarActualizacion = Task.CompletedTask;

    /// <summary>
    /// Dispara el flujo real de aplicar la actualización (descarga + reinicio) en background,
    /// sin bloquear el UI thread. Si <paramref name="banner"/> no es null (overlay banner),
    /// marca <see cref="ActualizacionBannerViewModel.OperacionEnCurso"/> para deshabilitar el
    /// botón mientras dura la operación, y lo despeja si el flujo falla (si tiene éxito, la app
    /// se reinicia y el flag deja de importar).
    /// </summary>
    private void DispararAplicarActualizacion(ActualizacionBannerViewModel? banner)
    {
        if (banner is not null)
            _uiDispatcher.Post(() => banner.OperacionEnCurso = true);

        _tareaAplicarActualizacion = Task.Run(async () =>
        {
            bool exito = false;
            try
            {
                exito = await _coordinadorActualizacion.AplicarActualizacionAsync();
            }
            catch (Exception)
            {
                // Silencio: no interrumpir la UI si aplicar la actualización falla.
                // AplicarActualizacionAsync ya atrapa sus propios errores; este catch es
                // defensivo por si algo inesperado escapa del flujo.
            }

            if (banner is not null && !exito)
                _uiDispatcher.Post(() => banner.OperacionEnCurso = false);
        });
    }

    public void MostrarLogin()
    {
        CurrentViewModel = new LoginViewModel(_authService, this, _infoApp);
    }

    /// <summary>
    /// Pantalla de bloqueo pre-login (Inc 7 Fase B): la API no tiene licencia activada.
    /// Se usa tanto en el arranque (InicializarAsync) como ante un 423 en cualquier request
    /// (ver ApiSession.LicenciaDesactivada, cableado en App.axaml.cs). Idempotente: varias
    /// requests concurrentes pueden recibir 423 y disparar el evento varias veces — si el
    /// bloqueo ya está en pantalla no se recrea el VM (evita perder lo que el usuario ya
    /// pegó/escribió y una navegación redundante), igual que MostrarLogin no se re-invoca
    /// para SesionVencida cuando el aviso ya cambia el mensaje sobre el mismo VM.
    /// </summary>
    public void MostrarBloqueoLicencia()
    {
        if (CurrentViewModel is BloqueoLicenciaViewModel)
            return;

        var bloqueo = new BloqueoLicenciaViewModel(_licenciaService);
        bloqueo.LicenciaActivada += () => _uiDispatcher.Post(MostrarLogin);
        CurrentViewModel = bloqueo;
    }

    /// <summary>
    /// Flujo "No puedo entrar / resetear Admin" (Inc 7 Fase B), abierto desde el login.
    /// </summary>
    public void MostrarReset()
    {
        var reset = new ResetAdminViewModel(_resetAdminService);
        reset.Volver += () => _uiDispatcher.Post(MostrarLogin);
        CurrentViewModel = reset;
    }

    /// <summary>
    /// Navega al login mostrando un aviso (ej. "Sesión vencida, ingresá de nuevo.").
    /// Lo cablea App.axaml.cs al evento ApiSession.SesionVencida (spec 3b, OQ-4).
    /// </summary>
    public void MostrarLoginConAviso(string aviso)
    {
        var login = new LoginViewModel(_authService, this, _infoApp);
        login.MensajeError = aviso;
        CurrentViewModel = login;
    }

    public void MostrarContenidoPrincipal()
    {
        // Navega al shell principal con menú lateral, que a su vez usa INavigationService
        // para manejar la región de contenido del catálogo.
        _navigation.Navegar<ShellMainViewModel>();
        CurrentViewModel = _navigation.Actual;

        // Navega a la pantalla de bienvenida como contenido inicial de la región central.
        // Orden crítico: Navegar<ShellMainViewModel>() dispara el evento Cambiado del
        // INavigationService, pero el handler en ShellMainViewModel descarta la asignación
        // porque ReferenceEquals(Actual, this) es true en ese momento (Actual ES el shell) —
        // evita que el shell se contenga a sí mismo. Recién después de fijar CurrentViewModel
        // = Actual (el shell ya es el contenido externo) navegamos a InicioViewModel: ahora
        // Actual pasa a ser InicioViewModel, el guard ya no aplica, y el handler del shell
        // asigna CurrentContent = InicioViewModel. Así la región central deja de quedar vacía.
        _navigation.Navegar<InicioViewModel>();
    }
}
