using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Endpoints;

public static class BackupsEndpoints
{
    public static IEndpointRouteBuilder MapBackupsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/backups", async (ServicioConsultaBackups servicio) =>
            Results.Ok(await servicio.ListarAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        app.MapGet("/backups/{id:int}/contenido",
            async (int id, ServicioConsultaBackups servicio, IUserDataPathProvider paths) =>
        {
            var (rutaCompleta, nombreArchivo) =
                await servicio.ResolverArchivoParaDescargaAsync(id, paths.GetBackupsDirectory());
            return Results.File(rutaCompleta, "application/octet-stream", nombreArchivo);
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        app.MapGet("/backups/salud", async (ServicioConsultaBackups servicio) =>
            Results.Ok(await servicio.ObtenerSaludAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
