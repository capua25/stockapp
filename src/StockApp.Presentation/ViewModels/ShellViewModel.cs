using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Auth;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Shell de navegación. Decide qué pantalla mostrar en función del estado de la app:
/// primer arranque → login → contenido principal.
/// </summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly IPrimerArranqueService _primerArranqueService;
    private readonly IAuthService           _authService;
    private readonly IUsuarioService        _usuarioService;

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    public ShellViewModel(
        IPrimerArranqueService primerArranqueService,
        IAuthService           authService,
        IUsuarioService        usuarioService)
    {
        _primerArranqueService = primerArranqueService;
        _authService           = authService;
        _usuarioService        = usuarioService;
    }

    /// <summary>
    /// Debe llamarse una sola vez al arrancar la app. Decide el primer VM a mostrar.
    /// </summary>
    public async Task InicializarAsync()
    {
        if (await _primerArranqueService.RequiereCrearAdminAsync())
            MostrarPrimerArranque();
        else
            MostrarLogin();
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
        CurrentViewModel = new MainWindowViewModel();
    }
}
