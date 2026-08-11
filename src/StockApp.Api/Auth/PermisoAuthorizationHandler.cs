using Microsoft.AspNetCore.Authorization;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

/// <summary>
/// Barrera HTTP de permisos (spec 2026-08-10). Reemplaza el RequireClaim fijo por rol que
/// Program.cs derivaba de AuthorizationService.TienePermiso al arrancar — ahora resuelve
/// contra los permisos reales del usuario en cada request. Scoped: inyecta IProveedorPermisos
/// (Singleton) y lee los claims del usuario actual desde el AuthorizationHandlerContext.
///
/// Orden crítico (Global Constraints): el corte por PermisosEstructuralesAdmin va SIEMPRE
/// antes de consultar el proveedor — un Operador nunca llega a esa consulta para uno de los
/// 4 permisos estructurales, sin importar qué hubiera en la tabla.
/// </summary>
public class PermisoAuthorizationHandler : AuthorizationHandler<PermisoRequirement>
{
    private readonly IProveedorPermisos _proveedor;

    public PermisoAuthorizationHandler(IProveedorPermisos proveedor) => _proveedor = proveedor;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermisoRequirement requirement)
    {
        var rolClaim = context.User.FindFirst(StockAppClaimTypes.Rol)?.Value;
        if (rolClaim is null || !Enum.TryParse<RolUsuario>(rolClaim, out var rol))
            return;

        if (rol == RolUsuario.Admin)
        {
            context.Succeed(requirement);
            return;
        }

        if (AuthorizationService.PermisosEstructuralesAdmin.Contains(requirement.Permiso))
            return; // Operador nunca — corte antes de tocar el proveedor.

        var usuarioIdClaim = context.User.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
        if (usuarioIdClaim is null || !int.TryParse(usuarioIdClaim, out var usuarioId))
            return;

        var permisos = await _proveedor.ObtenerAsync(usuarioId);
        if (permisos.Contains(requirement.Permiso))
            context.Succeed(requirement);
    }
}
