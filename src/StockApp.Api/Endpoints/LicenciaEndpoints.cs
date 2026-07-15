using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record LicenciaEstadoResponse(bool Activada, string CodigoMaquina);
public record ActivarLicenciaRequest(string? Licencia);

public static class LicenciaEndpoints
{
    public static IEndpointRouteBuilder MapLicenciaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/licencia");

        // Anónimo por diseño: es pre-login. El código de máquina es público.
        group.MapGet("/estado", (EstadoLicencia estado) =>
            Results.Ok(new LicenciaEstadoResponse(estado.Activada, estado.CodigoMaquina)));

        group.MapPost("/activar", async (
            ActivarLicenciaRequest request,
            ServicioLicencia servicio,
            EstadoLicencia estado,
            IUsuarioRepository usuarios,
            IAuditLogger audit) =>
        {
            if (string.IsNullOrWhiteSpace(request.Licencia))
                return Results.Problem(
                    title: "La licencia es obligatoria.",
                    statusCode: StatusCodes.Status400BadRequest);

            var resultado = await servicio.ActivarAsync(request.Licencia);

            if (resultado == ResultadoValidacionLicencia.Valida)
            {
                await AuditarAsync(usuarios, audit,
                    AccionAuditada.ActivacionLicencia, "Activación de licencia exitosa.");
                return Results.Ok(new LicenciaEstadoResponse(estado.Activada, estado.CodigoMaquina));
            }

            await AuditarAsync(usuarios, audit,
                AccionAuditada.IntentoActivacionLicenciaFallido,
                $"Intento de activación fallido: {resultado}.");

            return Results.Problem(
                title: MotivoDe(resultado),
                statusCode: StatusCodes.Status400BadRequest);
        }).RequireRateLimiting("licenciamiento");

        return app;
    }

    private static string MotivoDe(ResultadoValidacionLicencia resultado) => resultado switch
    {
        ResultadoValidacionLicencia.FormatoInvalido => "El texto de la licencia no tiene un formato válido.",
        ResultadoValidacionLicencia.FirmaInvalida   => "La firma de la licencia no es válida.",
        ResultadoValidacionLicencia.MaquinaDistinta => "La licencia fue emitida para otra máquina.",
        ResultadoValidacionLicencia.FingerprintIlegible =>
            "No se pudo leer la identificación de esta máquina. Contactá a soporte.",
        _ => "Licencia inválida.",
    };

    // Los eventos de licencia son anónimos (pre-login). LogAuditoria.UsuarioId es FK requerida:
    // se atribuye al primer admin (menor Id). Si no hay admin todavía, no se audita en DB.
    private static async Task AuditarAsync(
        IUsuarioRepository usuarios, IAuditLogger audit, AccionAuditada accion, string detalle)
    {
        var todos = await usuarios.ListarTodosAsync();
        var admin = todos
            .Where(u => u.Rol == RolUsuario.Admin)
            .OrderBy(u => u.Id)
            .FirstOrDefault();

        if (admin is not null)
            await audit.RegistrarAsync(admin.Id, accion, "Licencia", 0, detalle);
    }
}
