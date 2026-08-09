using StockApp.Application.Alertas;
using StockApp.Application.Authorization;

namespace StockApp.Api.Endpoints;

public record GuardarConfiguracionAlertasRequest(string? UrlWebhook, bool Habilitado);

public static class ConfiguracionAlertasEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracionAlertasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/configuracion/alertas");

        group.MapGet("/", async (ServicioConfiguracionAlertas servicio) =>
            Results.Ok(await servicio.ObtenerAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        group.MapPut("/", async (GuardarConfiguracionAlertasRequest request, ServicioConfiguracionAlertas servicio) =>
        {
            // La validación vive en el servicio (ArgumentException -> 400 vía DomainExceptionHandler),
            // igual que en el resto de los endpoints del repo. El handler queda tonto a propósito.
            await servicio.GuardarAsync(request.UrlWebhook, request.Habilitado);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        // Ping de prueba. Devuelve 200 con Exitoso = false ante configuración incompleta: no es un
        // error del cliente, es un diagnóstico — el resultado de la prueba ES la respuesta.
        // Nunca se devuelve el cuerpo de la respuesta remota (nota SSRF del spec).
        group.MapPost("/probar", async (ServicioConfiguracionAlertas servicio) =>
            Results.Ok(await servicio.ProbarAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
