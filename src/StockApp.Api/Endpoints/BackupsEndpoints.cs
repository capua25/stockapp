using StockApp.Api.Backups;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Exceptions;
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

        // POST /backups (disparo manual, fix/integridad-referencial): mismo permiso Admin-only
        // que los tres GET de arriba -- GestionarDiagnostico ya protege este recurso (un dump
        // completo de la base es el activo mas sensible del sistema), y disparar una corrida es,
        // si algo, MAS sensible que solo leerlas, nunca menos. Un permiso separado fragmentaria
        // la autorizacion sin que exista ninguna historia real de "Operador puede disparar pero
        // no leer" (o viceversa).
        //
        // Segunda barrera (defensa en profundidad, mismo criterio que ServicioConsultaBackups):
        // la policy HTTP de abajo ya exige GestionarDiagnostico, pero este endpoint no se apoya
        // solo en eso. DisparadorBackupManual es Singleton (no puede depender de ICurrentSession,
        // que es Scoped) asi que el auth.Verificar se hace aca, en el propio handler.
        //
        // 202 Accepted, no 200: EjecutorPgDumpProceso tiene un timeout de hasta 30 minutos (bases
        // con adjuntos grandes) -- devolver el resultado ya terminado bloquearia el request todo
        // ese tiempo. La corrida se dispara en background (DisparadorBackupManual, con su propio
        // IServiceScopeFactory, mismo patron que BackupProgramadoService) y se consulta despues
        // via GET /backups. 409 (ReglaDeNegocioException, mapeado por DomainExceptionHandler) si
        // IGuardiaCorridaBackup ya tiene una corrida en curso -- ver su doc para el porque.
        app.MapPost("/backups", (ICurrentSession session, IAuthorizationService auth, DisparadorBackupManual disparador) =>
        {
            auth.Verificar(session, Permisos.GestionarDiagnostico);

            var usuarioId = session.UsuarioActual!.Id;
            if (!disparador.Disparar(usuarioId))
                throw new ReglaDeNegocioException("Ya hay un backup en curso.");

            return Results.Accepted();
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
