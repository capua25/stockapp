using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Services;

public class AuditService : IAuditLogger
{
    private readonly AppDbContext _ctx;

    public AuditService(AppDbContext ctx) => _ctx = ctx;

    public async Task RegistrarAsync(
        int usuarioId, AccionAuditada accion, string entidad, int entidadId, string detalle)
    {
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = accion,
            Entidad   = entidad,
            EntidadId = entidadId,
            Detalle   = detalle
        });
        await _ctx.SaveChangesAsync();
    }
}
