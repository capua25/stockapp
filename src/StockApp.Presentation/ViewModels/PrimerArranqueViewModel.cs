using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// ViewModel de la pantalla de primer arranque (crear Admin inicial).
/// Después de la creación exitosa muestra una recomendación para crear un 2do Admin
/// de respaldo; el usuario puede crear uno o continuar directamente al login.
/// </summary>
public partial class PrimerArranqueViewModel : ViewModelBase
{
    private readonly IPrimerArranqueService _primerArranqueService;
    private readonly IAuthService           _authService;
    private readonly IUsuarioService        _usuarioService;
    private readonly ShellViewModel         _shell;

    // ── propiedades del formulario principal ─────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearAdminCommand))]
    private string _nombreUsuario = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearAdminCommand))]
    private string _contrasena = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearAdminCommand))]
    private string _confirmarContrasena = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearAdminCommand))]
    private bool _operacionEnCurso;

    // ── estado de recomendación de 2do Admin ─────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearSegundoAdminCommand))]
    private bool _mostrarRecomendacion2doAdmin;

    // ── propiedades del 2do Admin ────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearSegundoAdminCommand))]
    private string _nombreUsuario2doAdmin = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearSegundoAdminCommand))]
    private string _contrasena2doAdmin = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearSegundoAdminCommand))]
    private string _confirmarContrasena2doAdmin = string.Empty;

    [ObservableProperty]
    private string? _mensajeError2doAdmin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CrearSegundoAdminCommand))]
    private bool _operacion2doAdminEnCurso;

    // ── constructor ──────────────────────────────────────────────────────────

    public PrimerArranqueViewModel(
        IPrimerArranqueService primerArranqueService,
        IAuthService           authService,
        IUsuarioService        usuarioService,
        ShellViewModel         shell)
    {
        _primerArranqueService = primerArranqueService;
        _authService           = authService;
        _usuarioService        = usuarioService;
        _shell                 = shell;
    }

    // ── validación pública (testeable sin invocar el comando) ────────────────

    /// <summary>
    /// Valida el formulario del Admin inicial.
    /// Devuelve null si todo está OK, o el primer mensaje de error encontrado.
    /// </summary>
    public string? ValidarFormulario()
    {
        if (string.IsNullOrWhiteSpace(NombreUsuario))
            return "El nombre de usuario no puede estar vacío.";

        if (string.IsNullOrWhiteSpace(Contrasena))
            return "La contraseña no puede estar vacía.";

        if (Contrasena.Length < 6)
            return "La contraseña debe tener al menos 6 caracteres.";

        if (Contrasena != ConfirmarContrasena)
            return "Las contraseñas no coinciden.";

        return null;
    }

    // ── CanExecute del comando principal ─────────────────────────────────────

    private bool PuedeCrearAdmin()
        => !string.IsNullOrWhiteSpace(NombreUsuario)
        && !string.IsNullOrWhiteSpace(Contrasena)
        && Contrasena.Length >= 6
        && Contrasena == ConfirmarContrasena
        && !OperacionEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeCrearAdmin))]
    private async Task CrearAdminAsync()
    {
        MensajeError = ValidarFormulario();
        if (MensajeError is not null)
            return;

        OperacionEnCurso = true;

        try
        {
            await _primerArranqueService.CrearAdminInicialAsync(NombreUsuario, Contrasena);

            // Mostrar recomendación de 2do Admin en lugar de navegar inmediatamente.
            MostrarRecomendacion2doAdmin = true;
        }
        catch (ReglaDeNegocioException ex)
        {
            MensajeError = ex.Message;
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        finally
        {
            OperacionEnCurso = false;
        }
    }

    // ── 2do Admin ────────────────────────────────────────────────────────────

    private bool PuedeCrearSegundoAdmin()
        => MostrarRecomendacion2doAdmin
        && !string.IsNullOrWhiteSpace(NombreUsuario2doAdmin)
        && !string.IsNullOrWhiteSpace(Contrasena2doAdmin)
        && Contrasena2doAdmin.Length >= 6
        && Contrasena2doAdmin == ConfirmarContrasena2doAdmin
        && !Operacion2doAdminEnCurso;

    [RelayCommand(CanExecute = nameof(PuedeCrearSegundoAdmin))]
    private async Task CrearSegundoAdminAsync()
    {
        Operacion2doAdminEnCurso = true;
        MensajeError2doAdmin     = null;

        try
        {
            // Primero establecemos la sesión del Admin inicial para que
            // IUsuarioService pueda verificar autorización.
            await _authService.LoginAsync(NombreUsuario, Contrasena);

            await _usuarioService.AltaUsuarioAsync(
                NombreUsuario2doAdmin,
                nombreCompleto: null,
                Contrasena2doAdmin,
                RolUsuario.Admin);

            // Alta exitosa → navegar al login para que el usuario se autentique.
            _shell.MostrarLogin();
        }
        catch (Exception ex)
        {
            MensajeError2doAdmin = ex.Message;
        }
        finally
        {
            Operacion2doAdminEnCurso = false;
        }
    }

    // ── continuar sin 2do Admin ──────────────────────────────────────────────

    [RelayCommand]
    private void ContinuarSin2doAdmin()
    {
        _shell.MostrarLogin();
    }
}
