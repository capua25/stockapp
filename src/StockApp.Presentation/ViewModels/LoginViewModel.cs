using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// ViewModel de la pantalla de login.
/// Ante cualquier error de autenticación muestra un ÚNICO mensaje genérico
/// para no dar pistas sobre si el usuario existe (user-enumeration fix).
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService   _authService;
    private readonly ShellViewModel _shell;

    // ── propiedades observables ──────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private string _nombreUsuario = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EntrarCommand))]
    private bool _operacionEnCurso;

    // ── constructor ──────────────────────────────────────────────────────────

    public LoginViewModel(IAuthService authService, ShellViewModel shell)
    {
        _authService = authService;
        _shell       = shell;
    }

    // ── comando ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Habilitado solo cuando hay usuario, contraseña y no hay operación en curso.
    /// </summary>
    private bool PuedeEntrar()
        => !string.IsNullOrWhiteSpace(NombreUsuario)
        && !string.IsNullOrWhiteSpace(Contrasena)
        && !OperacionEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeEntrar))]
    private async Task EntrarAsync()
    {
        OperacionEnCurso = true;
        MensajeError     = null;

        try
        {
            var resultado = await _authService.LoginAsync(NombreUsuario, Contrasena);

            if (resultado.Exitoso)
            {
                _shell.MostrarContenidoPrincipal();
            }
            else
            {
                // FIX user-enumeration: siempre el mismo mensaje, sin importar el error interno.
                MensajeError = "Usuario o contraseña incorrectos.";
            }
        }
        finally
        {
            OperacionEnCurso = false;
        }
    }
}
