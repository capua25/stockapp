namespace StockApp.Application.Auth;

/// <summary>Contrato de autenticación. Permite mockear AuthService en tests de Presentation.</summary>
public interface IAuthService
{
    Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena);
    Task LogoutAsync();

    /// <summary>Permisos configurables efectivos del usuario autenticado (spec 2026-08-10),
    /// vía GET /auth/permisos. Como efecto colateral, puebla ICurrentSession.PermisosActuales —
    /// mismo criterio que LoginAsync puebla la sesión completa.</summary>
    Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync();
}
