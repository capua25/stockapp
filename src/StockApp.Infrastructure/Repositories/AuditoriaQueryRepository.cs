using Microsoft.EntityFrameworkCore;
using StockApp.Application.Auditoria;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Repositorio de solo lectura para el log de auditoría (EF Core / SQLite).
/// Aplica los filtros (usuario/fechas), ajusta FechaHasta a fin de día,
/// ordena por fecha descendente y proyecta a <see cref="AuditoriaItemDto"/>.
/// </summary>
public class AuditoriaQueryRepository : IAuditoriaQueryRepository
{
    private readonly AppDbContext _ctx;

    public AuditoriaQueryRepository(AppDbContext ctx) => _ctx = ctx;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(
        int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        // FechaHasta se ajusta a fin de día (23:59:59.9999999) para incluir todas las
        // entradas del día indicado, sin importar la hora con que se pasó.
        var fechaHastaFinDia = fechaHasta?.Date.AddDays(1).AddTicks(-1);

        // El Select con l.Usuario!.NombreUsuario genera el JOIN a Usuarios en SQL;
        // no hace falta Include explícito al proyectar a DTO.
        return await _ctx.LogsAuditoria
            .Where(l => (usuarioId == null || l.UsuarioId == usuarioId)
                     && (fechaDesde == null || l.Fecha >= fechaDesde)
                     && (fechaHastaFinDia == null || l.Fecha <= fechaHastaFinDia))
            .OrderByDescending(l => l.Fecha)
            .Select(l => new AuditoriaItemDto(
                l.Fecha,
                l.Usuario!.NombreUsuario,
                l.Accion,
                l.Entidad,
                l.EntidadId,
                l.Detalle))
            .ToListAsync();
    }
}
