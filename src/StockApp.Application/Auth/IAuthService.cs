namespace StockApp.Application.Auth;

/// <summary>Contrato de autenticación. Permite mockear AuthService en tests de Presentation.</summary>
public interface IAuthService
{
    Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena);
    Task LogoutAsync();
}
