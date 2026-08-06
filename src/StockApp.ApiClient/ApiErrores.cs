// src/StockApp.ApiClient/ApiErrores.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

/// <summary>Shape del body 201 `{ id }` que emiten los POST de la API (sin Location).</summary>
internal sealed record IdCreado(int Id);

/// <summary>
/// Proyección del problem+json (RFC 7807) de la API. Además de title/detail/status,
/// DomainExceptionHandler agrega extensiones estructuradas para StockInsuficienteException
/// (productoId/stockActual/cantidadSolicitada — Task 5 de este plan), para
/// ValidacionImportacionException (errors: diccionario "Tipo[i].Campo" → mensajes — F5d Task 4,
/// fix de review: sin esto la UI no puede resaltar la celda exacta del error de confirmación) y
/// para AnulacionRequierePagoAutomaticoConfirmadoException (gastoId/montoPagoAutomatico —
/// anulación en cascada del pago automático de contado, unificación Contado ⇒ pago automático).
/// ReadFromJsonAsync usa defaults Web: camelCase + case-insensitive.
/// </summary>
internal sealed record ProblemaJson(
    string? Title,
    string? Detail,
    int? Status,
    int? ProductoId,
    decimal? StockActual,
    decimal? CantidadSolicitada,
    int? GastoId,
    decimal? MontoPagoAutomatico,
    [property: JsonPropertyName("errors")] IReadOnlyDictionary<string, string[]>? Errores);

/// <summary>
/// Traducción centralizada HTTP → excepciones de dominio (spec 3b): UN solo lugar, los
/// 10 XxxApiClient no repiten switches de status ni try/catch de transporte.
/// </summary>
internal static class ApiErrores
{
    /// <summary>
    /// Ejecuta el envío HTTP convirtiendo los fallos de transporte en
    /// <see cref="ServidorNoDisponibleException"/> (conexión rechazada, DNS, timeout).
    /// <paramref name="ct"/> es OPCIONAL — los ~10 XxxApiClient que no pasan un token propio
    /// siguen con el comportamiento de siempre (toda cancelación es timeout). BackupsApiClient
    /// (Task Backups) es el primero en pasar un ct real, cancelable desde la UI: con ct
    /// explícito, una cancelación deliberada del CALLER se distingue de un timeout real y se
    /// repropaga tal cual en vez de envolverse.
    /// </summary>
    internal static async Task<HttpResponseMessage> EnviarAsync(
        Func<Task<HttpResponseMessage>> enviar, CancellationToken ct = default)
    {
        try
        {
            return await enviar();
        }
        catch (HttpRequestException ex)
        {
            throw new ServidorNoDisponibleException(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelación deliberada del caller (ej. BackupsApiClient.DescargarAsync con el
            // CancellationToken del botón "Cancelar" de MantenimientoViewModel) — se repropaga
            // tal cual para que el caller la distinga de una falla real del servidor. Mismo
            // criterio que EjecutorPgDumpProceso.EjecutarAsync del lado servidor (Task 3):
            // catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested).
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient.Timeout vencido, o cualquier cancelación SIN que el ct propio del
            // caller esté marcado — se sigue tratando como indisponibilidad del servidor.
            // Comportamiento SIN CAMBIOS para los ~10 ApiClients que no pasan ct (entran acá
            // con ct = default, que nunca está cancelado).
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
            HttpStatusCode.BadRequest      => CrearBadRequest(problema, mensaje),
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
    /// flujo "¿forzar salida?" de MovimientoRegistroViewModelBase. 409 con extensiones de
    /// pago automático → AnulacionRequierePagoAutomaticoConfirmadoException, para que el
    /// ViewModel distinga "falta confirmar la anulación en cascada" de un 409 genérico y
    /// pueda ofrecer el diálogo de confirmación en vez de solo informar el error. Cualquier
    /// otro 409 → ReglaDeNegocioException con el detail del servidor.
    /// </summary>
    private static Exception CrearConflicto(ProblemaJson? problema, string mensaje)
    {
        if (problema is { ProductoId: int productoId, StockActual: decimal stockActual, CantidadSolicitada: decimal cantidadSolicitada })
            return new StockInsuficienteException(productoId, stockActual, cantidadSolicitada);

        if (problema is { GastoId: int gastoId, MontoPagoAutomatico: decimal montoPagoAutomatico })
            return new AnulacionRequierePagoAutomaticoConfirmadoException(gastoId, montoPagoAutomatico);

        return new ReglaDeNegocioException(mensaje);
    }

    /// <summary>
    /// 400 con extensión "errors" (diccionario "Tipo[i].Campo" → mensajes, F5d) →
    /// ValidacionImportacionException reconstruida con el MISMO diccionario que armó
    /// DomainExceptionHandler — preserva el detalle por campo para que la UI del importador
    /// resalte la celda exacta. Cualquier otro 400 (validaciones simples de los demás
    /// endpoints) → ArgumentException plano con el detail, como siempre.
    /// </summary>
    private static Exception CrearBadRequest(ProblemaJson? problema, string mensaje)
    {
        if (problema is { Errores: { Count: > 0 } errores })
            return new ValidacionImportacionException(errores);

        return new ArgumentException(mensaje);
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
