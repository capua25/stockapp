using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación simple de <see cref="IAuthorizationService"/>:
/// tabla de acciones permitidas por rol. Admin tiene acceso a todo; Operador solo
/// a las acciones operativas (catálogo, movimientos, recálculo).
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    // Acciones habilitadas para Operador. VerReportes y GestionarUsuarios están
    // deliberadamente AUSENTES: son exclusivas de Admin (fail-closed por diseño).
    private static readonly HashSet<string> AccionesOperador =
    [
        Permisos.GestionarCatalogo,
        Permisos.RegistrarMovimientos,
        Permisos.RecalcularStock,
    ];

    public void Verificar(RolUsuario? rolActual, string accion)
    {
        if (rolActual is null)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        if (rolActual == RolUsuario.Admin)
            return; // Admin puede todo

        if (!AccionesOperador.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");
    }
}
