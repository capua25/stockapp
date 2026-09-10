using Microsoft.EntityFrameworkCore;
using StockApp.Application.Auditoria;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Repositorio de solo lectura para el log de auditoría (EF Core / PostgreSQL, Npgsql).
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
        // fechaHasta llega como INSTANTE UTC ya convertido por el ViewModel (medianoche local
        // de Uruguay = 03:00Z). Truncar con .Date reancla el fin de día a medianoche UTC y
        // pierde las últimas 3hs del día local; el fin de rango se calcula sumando un día
        // completo al instante recibido, sin tocar su hora.
        var fechaHastaFinDia = fechaHasta?.AddDays(1).AddTicks(-1);

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
