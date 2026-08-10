using StockApp.Application.Alertas;
using StockApp.Application.Authorization;

namespace StockApp.Api.Endpoints;

public record GuardarConfiguracionAlertasRequest(string? UrlWebhook, bool Habilitado);

/// <summary>Body OPCIONAL de POST /probar: la URL que el usuario tiene en pantalla.</summary>
public record ProbarConfiguracionAlertasRequest(string? UrlWebhook);

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

        // Ping de prueba. Devuelve 200 con Exitoso = false ante configuración incompleta O ante un
        // webhook que rechaza/no responde: no es un error del cliente, es un diagnóstico — el
        // resultado de la prueba ES la respuesta. Nunca se devuelve el cuerpo de la respuesta
        // remota (nota SSRF del spec), solo el status code y un mensaje propio.
        //
        // El body es OPCIONAL (parámetro nullable): sin body se prueba la configuración guardada;
        // con body se prueba la URL que el usuario tiene en pantalla, sin persistirla. La
        // validación de esa URL vive en el servicio, igual que la de PUT.
        group.MapPost("/probar", async (
                ProbarConfiguracionAlertasRequest? request,
                ServicioConfiguracionAlertas servicio,
                CancellationToken ct) =>
            Results.Ok(await servicio.ProbarAsync(request?.UrlWebhook, ct)))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
