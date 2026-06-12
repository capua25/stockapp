using StockApp.Application.Auditoria;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Contrato de persistencia para la consulta del log de auditoría (solo lectura).
/// El repo aplica los filtros (usuario/fechas), ajusta FechaHasta a fin de día
/// y ordena por fecha descendente (C4). El service solo autoriza y delega.
/// </summary>
public interface IAuditoriaQueryRepository
{
    /// <summary>
    /// Entradas del log de auditoría filtradas por usuario y rango de fechas,
    /// ordenadas por fecha descendente. El ajuste de FechaHasta a fin de día,
    /// el filtrado y el orden los realiza el repo. Retorna lista vacía si no hay registros.
    /// </summary>
    Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(
        int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta);
}
