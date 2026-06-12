using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Auditoria;

/// <summary>
/// Servicio de consulta del log de auditoría.
/// Patrón: auth fail-closed → repo. El filtrado, el ajuste de FechaHasta a fin
/// de día y el orden por fecha descendente son responsabilidad del repo (C4).
/// </summary>
public class AuditoriaQueryService : IAuditoriaQueryService
{
    private readonly IAuditoriaQueryRepository _repo;
    private readonly ICurrentSession           _session;
    private readonly IAuthorizationService     _auth;

    public AuditoriaQueryService(
        IAuditoriaQueryRepository repo,
        ICurrentSession           session,
        IAuthorizationService     auth)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
    }

    public async Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(
        int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session.RolActual, Permisos.VerReportes);

        // Parámetros se pasan TAL CUAL (incluida FechaHasta cruda): el filtrado,
        // el ajuste de fin de día y el orden los realiza el repo (C4).
        return await _repo.ObtenerLogAsync(usuarioId, fechaDesde, fechaHasta);
    }
}
