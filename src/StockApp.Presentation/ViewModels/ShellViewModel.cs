using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Auth;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;

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
        CoordinadorActualizacion coordinadorActualizacion)
    {
        _primerArranqueService    = primerArranqueService;
        _authService              = authService;
        _usuarioService           = usuarioService;
        _navigation               = navigation;
        _coordinadorActualizacion = coordinadorActualizacion;
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

        // Asignamos el overlay desde el thread del pool.
        // Avalonia hace marshal automático del PropertyChanged al UI thread cuando hay binding
        // activo. En tests sin app, la asignación directa es igualmente segura.
        OverlayActualizacion = CoordinadorActualizacion.ResolverOverlayViewModel(
            _coordinadorActualizacion.AccionUxActual);
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
        Program.LogTrace("Shell", "MostrarContenidoPrincipal inicio");

        // Navega al shell principal con menú lateral, que a su vez usa INavigationService
        // para manejar la región de contenido del catálogo.
        _navigation.Navegar<ShellMainViewModel>();

        Program.LogTrace("Shell", $"Navegado. Actual={_navigation.Actual?.GetType().Name}");

        CurrentViewModel = _navigation.Actual;

        Program.LogTrace("Shell", "CurrentViewModel asignado");
    }
}
