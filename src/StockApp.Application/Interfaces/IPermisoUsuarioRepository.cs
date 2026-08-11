namespace StockApp.Application.Interfaces;

/// <summary>
/// Persistencia cruda de PermisoUsuario. Sin cache, sin conocer los 4 permisos estructurales —
/// esa lógica vive en AuthorizationService/IProveedorPermisos, más arriba en la pila. Un SELECT
/// sin filas devuelve un conjunto vacío, nunca null (fail-closed, spec decisión 3).
/// </summary>
public interface IPermisoUsuarioRepository
{
    /// <summary>Permisos configurables actuales del usuario. Vacío si no hay filas.</summary>
    Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId);

    /// <summary>
    /// Reemplaza el set completo del usuario: borra las filas existentes e inserta las nuevas
    /// dentro de una única transacción. Una colección vacía deja al usuario sin permisos.
    /// </summary>
    Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos);
}
