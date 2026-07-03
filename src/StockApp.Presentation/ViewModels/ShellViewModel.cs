using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Auth;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Shell de navegación. Decide qué pantalla mostrar en función del estado de la app:
/// primer arranque → login → contenido principal (ShellMainViewModel con menú lateral).
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly IPrimerArranqueService  _primerArranqueService;
    private readonly IAuthService            _authService;
    private readonly IUsuarioService         _usuarioService;
    private readonly INavigationService      _navigation;
    private readonly CoordinadorActualizacion _coordinadorActualizacion;
    private readonly IUiDispatcher           _uiDispatcher;

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
        IPrimerArranqueService  primerArranqueService,
        IAuthService            authService,
        IUsuarioService         usuarioService,
        INavigationService      navigation,
        CoordinadorActualizacion coordinadorActualizacion,
        IUiDispatcher           uiDispatcher)
    {
        _primerArranqueService    = primerArranqueService;
        _authService              = authService;
        _usuarioService           = usuarioService;
        _navigation               = navigation;
        _coordinadorActualizacion = coordinadorActualizacion;
        _uiDispatcher             = uiDispatcher;
    }

    /// <summary>
    /// Debe llamarse una sola vez al arrancar la app. Decide el primer VM a mostrar.
    /// Dispara el chequeo de actualizaciones en background sin bloquear el arranque.
    /// </summary>
    public async Task InicializarAsync()
    {
        if (await _primerArranqueService.RequiereCrearAdminAsync())
            MostrarPrimerArranque();
        else
            MostrarLogin();

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
        _uiDispatcher.Post(() => OverlayActualizacion = overlay);
    }

    public void MostrarPrimerArranque()
    {
        CurrentViewModel = new PrimerArranqueViewModel(
            _primerArranqueService,
            _authService,
            _usuarioService,
            this);
    }

    public void MostrarLogin()
    {
        CurrentViewModel = new LoginViewModel(_authService, this);
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
