namespace StockApp.Application.Auditoria;

/// <summary>
/// Servicio de consulta del log de auditoría. Patrón: auth → repo.
/// Requiere el permiso <c>reportes.ver</c> (negado a Operador).
/// </summary>
public interface IAuditoriaQueryService
{
    /// <summary>
    /// Log de auditoría filtrado por usuario y rango de fechas. El filtrado,
    /// el ajuste de FechaHasta a fin de día y el orden por fecha descendente
    /// los realiza el repo (C4). El service solo autoriza y delega.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso para ver reportes.</exception>
    Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(
        int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta);
}
