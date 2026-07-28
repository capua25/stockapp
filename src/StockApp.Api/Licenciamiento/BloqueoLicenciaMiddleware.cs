using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Licenciamiento;

/// <summary>
/// Sin licencia activa, TODO devuelve 423 Locked salvo /licencia/*, /auth/reset-admin/* (los
/// flujos pre-login de activación y recuperación) y, desde el fix del review final de Entrega 1,
/// /auth/login y /backups. El estado se lee del singleton EstadoLicencia — costo cero por
/// request cuando la licencia está activa.
///
/// POR QUÉ /auth/login: sin esta excepción, con licencia vencida el admin no podía ni
/// autenticarse (423 en el login) para llegar a /backups -- los dumps quedaban inalcanzables
/// justo cuando más se necesitan, en un servidor sin acceso remoto (ver decisión del usuario).
/// Acotado a propósito: este middleware corre ANTES de UseAuthentication/UseAuthorization
/// (ver Program.cs), así que exime por RUTA, no por identidad -- un token válido obtenido acá
/// NO sirve para pasar el bloqueo de ningún otro endpoint, que sigue devolviendo 423
/// incondicionalmente. Autenticarse con licencia vencida sólo abre el camino hacia /backups
/// (que además exige el permiso GestionarDiagnostico en la capa de Application); el resto del
/// sistema sigue completamente bloqueado.
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
        || path.StartsWithSegments("/backups");
}
