using StockApp.Application.Auditoria;
using StockApp.Application.Authorization;

namespace StockApp.Api.Endpoints;

public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapAuditoriaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auditoria", async (
            int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta, IAuditoriaQueryService auditoria) =>
            Results.Ok(await auditoria.ObtenerLogAsync(usuarioId, fechaDesde, fechaHasta)))
            .RequireAuthorization(Permisos.VerReportes);

        return app;
    }
}
