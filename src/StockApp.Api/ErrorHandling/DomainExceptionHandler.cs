using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StockApp.Domain.Exceptions;

namespace StockApp.Api.ErrorHandling;

/// <summary>
/// Mapeo centralizado de excepciones de dominio/aplicación a status HTTP + ProblemDetails
/// (Fase 2b, sección "Manejo de errores" del spec). Los endpoints no hacen try/catch:
/// cualquier excepción no capturada por Minimal API llega acá vía app.UseExceptionHandler().
/// </summary>
public class DomainExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            // Fase 3a, D4: única fuente de 404/409 de negocio. StockInsuficienteException
            // hereda de ReglaDeNegocioException (Task 1) así que ya matchea acá sin caso propio.
            // InvalidOperationException/KeyNotFoundException genéricas del BCL YA NO las lanza
            // ningún servicio de StockApp.Application — si aparecen, es un error no anticipado
            // y caen al 500 fail-closed del caso '_' de abajo, no a un 409/404 que sugeriría
            // una regla de negocio real.
            EntidadNoEncontradaException => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ReglaDeNegocioException      => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            // F5c: errores de validación estructurada del payload de /confirmar (referencias
            // nominales que no resuelven, campos obligatorios ausentes). Mismo 400 que
            // ArgumentException, pero con el diccionario Errores agregado más abajo.
            ValidacionImportacionException => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            ArgumentException            => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            // Entrega 2 (diagnóstico): el directorio de logs existe pero el servidor no puede
            // leerlo (permisos del filesystem, no del usuario). 503 en vez de 403 porque el
            // problema es del entorno del servidor, no una falta de permiso del cliente — y a
            // diferencia del 500 genérico de abajo, el detail NO se borra: el admin necesita ver
            // la ruta para poder pasársela a quien tenga acceso al servidor.
            DirectorioLogsInaccesibleException => (StatusCodes.Status503ServiceUnavailable, "Servicio no disponible."),
            UnauthorizedAccessException  => (StatusCodes.Status403Forbidden, "Prohibido."),
            // Binding fallido de Minimal API (ej. valor de query param que no matchea un enum):
            // input inválido del cliente, nunca un 500. Se respeta el StatusCode propio de la
            // excepción (normalmente 400, pero Kestrel puede usar variantes como 413/431).
            BadHttpRequestException ex   => (ex.StatusCode, "Solicitud inválida."),
            _                            => (StatusCodes.Status500InternalServerError, "Error interno."),
        };

        // Los 4xx son fallas de negocio esperables (Warning). El 500 es el caso que no
        // anticipamos: va como Error porque es el que alguien va a tener que diagnosticar
        // despues, sin acceso al servidor, leyendo el ZIP de logs.
        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Error no controlado en {Ruta}", httpContext.Request.Path);
        else
            _logger.LogWarning(exception, "Falla de negocio {Status} en {Ruta}",
                status, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        var contexto = new ProblemDetailsContext
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
        };

        // Fase 3b: datos estructurados para que el cliente HTTP del desktop reconstruya
        // StockInsuficienteException tipada (el flujo "¿forzar salida?" del ViewModel usa
        // StockResultante). Cambio aditivo: title/detail/status no cambian.
        if (exception is StockInsuficienteException stock)
        {
            contexto.ProblemDetails.Extensions["productoId"]         = stock.ProductoId;
            contexto.ProblemDetails.Extensions["stockActual"]        = stock.StockActual;
            contexto.ProblemDetails.Extensions["cantidadSolicitada"] = stock.CantidadSolicitada;
        }

        // F5c: mismo shape que Microsoft.AspNetCore.Http.Results.ValidationProblem produciría
        // (un objeto "errors" con clave "Tipo[índice].Campo" → array de mensajes), sin poder
        // usar ese helper acá porque IExceptionHandler no devuelve un IResult.
        if (exception is ValidacionImportacionException validacion)
        {
            contexto.ProblemDetails.Extensions["errors"] = validacion.Errores;
        }

        return await problemDetailsService.TryWriteAsync(contexto);
    }
}
