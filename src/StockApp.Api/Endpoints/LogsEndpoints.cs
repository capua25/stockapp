using System.IO.Compression;
using Microsoft.AspNetCore.Http.Features;
using StockApp.Application.Authorization;
using StockApp.Application.Logs;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Endpoints;

public static class LogsEndpoints
{
    public static IEndpointRouteBuilder MapLogsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/logs", (ServicioConsultaLogs servicio, IUserDataPathProvider paths) =>
            Results.Ok(servicio.ObtenerResumen(paths.GetLogsDirectory())))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        // Un unico ZIP con todos los archivos, sin parametro de nombre: sin parametro no
        // hay superficie de path traversal. Se arma por streaming sobre el Response.Body,
        // asi no materializamos el zip completo ni en memoria ni en disco temporal.
        app.MapGet("/logs/contenido", (HttpContext context, ServicioConsultaLogs servicio, IUserDataPathProvider paths) =>
        {
            var archivos = servicio.ResolverArchivosParaZip(paths.GetLogsDirectory());
            var nombreZip = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.zip";

            // ZipArchive no tiene API async en el BCL: Dispose() escribe el directorio central
            // del ZIP de forma sincronica. Kestrel (y TestServer, igual) bloquean IO sincrono
            // sobre el body por defecto -- sin esto la descarga rompe siempre, tambien en
            // produccion, justo el dia que se la necesita.
            var featureIOSincronica = context.Features.Get<IHttpBodyControlFeature>();
            if (featureIOSincronica is not null) featureIOSincronica.AllowSynchronousIO = true;

            return Results.Stream(async salida =>
            {
                using var zip = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var ruta in archivos)
                {
                    var entrada = zip.CreateEntry(Path.GetFileName(ruta), CompressionLevel.Optimal);
                    // FileShare.ReadWrite es obligatorio: Serilog tiene abierto el archivo
                    // del dia en curso y sin esto la descarga falla justo cuando mas importa.
                    await using var origen = new FileStream(
                        ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    await using var destino = entrada.Open();
                    await origen.CopyToAsync(destino);
                }
            }, "application/zip", nombreZip);
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
