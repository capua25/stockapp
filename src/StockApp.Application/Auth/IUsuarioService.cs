using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>Contrato del ABM de usuarios. Permite mockear UsuarioService en tests de Presentation.</summary>
public interface IUsuarioService
{
    Task AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol);
    Task BajaLogicaAsync(int usuarioId);
    Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol);
    Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null);
}
