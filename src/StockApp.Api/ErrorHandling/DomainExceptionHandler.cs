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
            // Fase 3a, D4: excepciones de dominio propias — sustituyen gradualmente a las
            // genéricas del BCL de abajo. StockInsuficienteException hereda de
            // ReglaDeNegocioException (Task 1) así que ya matchea acá sin caso propio.
            EntidadNoEncontradaException => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ReglaDeNegocioException      => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            // TODO(Fase 3a, Task 10): eliminar estos dos casos cuando el barrido de
            // servicios de Application (Tasks 3-9) termine de reemplazarlos por los de arriba.
            // Hasta entonces conviven para no romper la suite mientras se migra servicio a servicio.
            InvalidOperationException    => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            KeyNotFoundException         => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ArgumentException            => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException  => (StatusCodes.Status403Forbidden, "Prohibido."),
            // Binding fallido de Minimal API (ej. valor de query param que no matchea un enum):
            // input inválido del cliente, nunca un 500. Se respeta el StatusCode propio de la
            // excepción (normalmente 400, pero Kestrel puede usar variantes como 413/431).
            BadHttpRequestException ex   => (ex.StatusCode, "Solicitud inválida."),
            _                            => (StatusCodes.Status500InternalServerError, "Error interno."),
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
