using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Licenciamiento;

/// <summary>
/// Sin licencia activa, TODO devuelve 423 Locked salvo /licencia/* y /auth/reset-admin/*
/// (los flujos pre-login de activación y recuperación). El login incluido. El estado se lee
/// del singleton EstadoLicencia — costo cero por request cuando la licencia está activa.
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
        || path.StartsWithSegments("/auth/reset-admin");
}
