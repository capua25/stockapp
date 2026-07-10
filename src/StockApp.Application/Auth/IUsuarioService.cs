using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>Contrato del ABM de usuarios. Permite mockear UsuarioService en tests de Presentation.</summary>
public interface IUsuarioService
{
    /// <summary>Crea un usuario nuevo y devuelve su Id (Fase 3a, D2).</summary>
    Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol);
    Task BajaLogicaAsync(int usuarioId);
    Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol);
    Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null);

    /// <summary>Lista todos los usuarios (activos e inactivos). Requiere GestionarUsuarios (Fase 2b).</summary>
    Task<IReadOnlyList<UsuarioDto>> ListarAsync();
}
