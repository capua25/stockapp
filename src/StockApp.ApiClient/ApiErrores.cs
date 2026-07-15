// src/StockApp.ApiClient/ApiErrores.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

/// <summary>Shape del body 201 `{ id }` que emiten los POST de la API (sin Location).</summary>
internal sealed record IdCreado(int Id);

/// <summary>
/// Proyección del problem+json (RFC 7807) de la API. Además de title/detail/status,
/// DomainExceptionHandler agrega extensiones estructuradas para StockInsuficienteException
/// (productoId/stockActual/cantidadSolicitada — Task 5 de este plan).
/// ReadFromJsonAsync usa defaults Web: camelCase + case-insensitive.
/// </summary>
internal sealed record ProblemaJson(
    string? Title,
    string? Detail,
    int? Status,
    int? ProductoId,
    decimal? StockActual,
    decimal? CantidadSolicitada);

/// <summary>
/// Traducción centralizada HTTP → excepciones de dominio (spec 3b): UN solo lugar, los
/// 10 XxxApiClient no repiten switches de status ni try/catch de transporte.
/// </summary>
internal static class ApiErrores
{
    /// <summary>
    /// Ejecuta el envío HTTP convirtiendo los fallos de transporte en
    /// <see cref="ServidorNoDisponibleException"/> (conexión rechazada, DNS, timeout).
    /// </summary>
    internal static async Task<HttpResponseMessage> EnviarAsync(Func<Task<HttpResponseMessage>> enviar)
    {
        try
        {
            return await enviar();
        }
        catch (HttpRequestException ex)
        {
            throw new ServidorNoDisponibleException(ex);
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient.Timeout vencido: llega como TaskCanceledException (inner TimeoutException).
            // Los clients no pasan CancellationToken propio → toda cancelación es timeout.
            throw new ServidorNoDisponibleException(ex);
        }
    }

    /// <summary>
    /// Si el status no es exitoso, lanza la excepción de dominio correspondiente con el
    /// detail del problem+json como mensaje (los ViewModels muestran ex.Message tal cual).
    /// </summary>
    internal static async Task AsegurarExitoAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var problema = await LeerProblemaAsync(response);
        var mensaje = problema?.Detail
            ?? problema?.Title
            ?? $"El servidor respondió {(int)response.StatusCode}.";

        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound        => new EntidadNoEncontradaException(mensaje),
            HttpStatusCode.Conflict        => CrearConflicto(problema, mensaje),
            HttpStatusCode.BadRequest      => new ArgumentException(mensaje),
            HttpStatusCode.Forbidden       => new UnauthorizedAccessException(mensaje),
            HttpStatusCode.Unauthorized    => new UnauthorizedAccessException(mensaje),
            HttpStatusCode.TooManyRequests => new ReglaDeNegocioException(
                problema?.Detail ?? problema?.Title
                    ?? "Demasiados intentos, esperá un minuto y volvé a probar."),
            _ => new InvalidOperationException(
                $"Error inesperado del servidor ({(int)response.StatusCode}): {mensaje}"),
        };
    }

    /// <summary>
    /// 409 con extensiones de stock → StockInsuficienteException reconstruida con el MISMO
    /// constructor que usó el servidor (mensaje y StockResultante idénticos) — preserva el
    /// flujo "¿forzar salida?" de MovimientoRegistroViewModelBase. Cualquier otro 409 →
    /// ReglaDeNegocioException con el detail del servidor.
    /// </summary>
    private static Exception CrearConflicto(ProblemaJson? problema, string mensaje)
    {
        if (problema is { ProductoId: int productoId, StockActual: decimal stockActual, CantidadSolicitada: decimal cantidadSolicitada })
            return new StockInsuficienteException(productoId, stockActual, cantidadSolicitada);

        return new ReglaDeNegocioException(mensaje);
    }

    private static async Task<ProblemaJson?> LeerProblemaAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemaJson>();
        }
        catch (Exception)
        {
            // Body vacío o no-JSON (proxy, HTML de error): se cae al mensaje genérico.
            return null;
        }
    }
}
