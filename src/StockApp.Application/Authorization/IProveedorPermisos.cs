namespace StockApp.Application.Authorization;

/// <summary>
/// Resolución cacheada de los permisos configurables de un usuario (spec 2026-08-10). Misma
/// forma que IRevocadorTokens: una sola interfaz que junta lectura cacheada y escritura con
/// invalidación, en vez de separar en dos servicios. Componente de INFRAESTRUCTURA DE
/// RESOLUCIÓN (cache + repo), no de política: devuelve el SELECT crudo, sin Admin bypass y
/// sin conocer los 4 permisos estructurales — esa lógica de negocio vive en
/// AuthorizationService.Verificar, que nunca llama a esta interfaz directamente (lee un
/// resultado ya resuelto por el middleware). Nada viaja en el JWT: cada resolución consulta
/// el estado actual (de cache o de DB).
/// </summary>
public interface IProveedorPermisos
{
    /// <summary>Permisos configurables del usuario. Cache-first; SELECT contra
    /// PermisoUsuario solo en cache-miss.</summary>
    Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId);

    /// <summary>Reemplaza el set completo del usuario e invalida su entrada de cache
    /// en la misma operación.</summary>
    Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos);
}
