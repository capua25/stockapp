using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Domain.Exceptions;

namespace StockApp.Api.ErrorHandling;

/// <summary>
/// Mapeo centralizado de excepciones de dominio/aplicación a status HTTP + ProblemDetails
/// (Fase 2b, sección "Manejo de errores" del spec). Los endpoints no hacen try/catch:
/// cualquier excepción no capturada por Minimal API llega acá vía app.UseExceptionHandler().
/// </summary>
public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            StockInsuficienteException  => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            InvalidOperationException   => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            KeyNotFoundException        => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ArgumentException           => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Prohibido."),
            _                           => (StatusCodes.Status500InternalServerError, "Error interno."),
        };

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                // 500: nunca exponer exception.Message (fail-closed, spec "Manejo de errores").
                Detail = status == StatusCodes.Status500InternalServerError ? null : exception.Message,
            },
        });
    }
}
