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

    /// <summary>Permisos configurables actuales del usuario (spec 2026-08-10). Para un Admin,
    /// devuelve los 11 configurables completos (siempre los tiene). Requiere GestionarUsuarios.</summary>
    Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId);

    /// <summary>Reemplaza el set de permisos configurables del usuario. 400 si el usuario es
    /// Admin, 400 si algún permiso no está en la whitelist de configurables. Requiere
    /// GestionarUsuarios. Registra AccionAuditada.ModificacionPermisosUsuario.</summary>
    Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos);
}
