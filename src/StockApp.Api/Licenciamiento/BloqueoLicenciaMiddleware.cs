using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Licenciamiento;

/// <summary>
/// Sin licencia activa, TODO devuelve 423 Locked salvo /licencia/*, /auth/reset-admin/* (los
/// flujos pre-login de activación y recuperación), /auth/login, /backups (fix del review final
/// de Entrega 1) y, desde Entrega 2 Task 7, /logs. El estado se lee del singleton EstadoLicencia
/// — costo cero por request cuando la licencia está activa.
///
/// POR QUÉ /auth/login: sin esta excepción, con licencia vencida el admin no podía ni
/// autenticarse (423 en el login) para llegar a /backups -- los dumps quedaban inalcanzables
/// justo cuando más se necesitan, en un servidor sin acceso remoto (ver decisión del usuario).
///
/// POR QUÉ /logs: cuando la licencia vence es JUSTO cuando más se necesita poder mirar los
/// logs para diagnosticar por qué (fingerprint cambiado, licencia corrupta, etc.).
///
/// Acotado a propósito: este middleware corre ANTES de UseAuthentication/UseAuthorization
/// (ver Program.cs), así que exime por RUTA, no por identidad -- un token válido obtenido acá
/// NO sirve para pasar el bloqueo de ningún otro endpoint, que sigue devolviendo 423
/// incondicionalmente. Autenticarse con licencia vencida sólo abre el camino hacia /backups y
/// /logs (que además exigen el permiso GestionarDiagnostico en la capa de Application); el
/// resto del sistema sigue completamente bloqueado.
/// </summary>
public sealed class BloqueoLicenciaMiddleware
{
    private const int StatusLocked = 423;
    private readonly RequestDelegate _next;

    public BloqueoLicenciaMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(
        HttpContext context, EstadoLicencia estado, IProblemDetailsService problemDetails)
    {
        if (estado.Activada || EsRutaPermitida(context.Request.Path))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusLocked;
        context.Response.ContentType = "application/problem+json";
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails =
            {
                Status = StatusLocked,
                Title  = "Licencia no activada.",
                Detail = "El servidor no tiene una licencia válida activada. "
                       + "Activá la licencia desde la pantalla de bloqueo del cliente.",
            },
        });
    }

    private static bool EsRutaPermitida(PathString path)
        => path.StartsWithSegments("/licencia")
        || path.StartsWithSegments("/auth/reset-admin")
        || path.StartsWithSegments("/auth/login")
        || path.StartsWithSegments("/backups")
        || path.StartsWithSegments("/logs");
}
