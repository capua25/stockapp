using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockApp.Configurador.Servicios;

/// <summary>
/// Pega a GET / de la API (mismo endpoint anónimo que expone StockApp.Api, Program.cs:675) y
/// distingue los casos de la spec 2026-08-20, ampliada 2026-09-18 (ver ResultadoPruebaConexion).
///
/// Timeout: 12s, no 4s. El de 4s era deliberadamente corto porque el usuario está mirando la
/// ventana en vivo, pero no alcanza para el primer handshake TLS cuando Schannel tiene que
/// salir a construir la cadena de certificados (Let's Encrypt "Generation Y" / ISRG Root X1
/// cross-sign, no está en el trust store de Windows hasta que se cachea la primera vez) — ese
/// fue el bug real reportado, no "el servidor apagado". Formas de excepción verificadas
/// EMPÍRICAMENTE (no de memoria, ver reporte de la tarea): conexión rechazada y DNS llegan como
/// HttpRequestException con InnerException SocketException (SocketErrorCode distingue los dos
/// casos); TLS inválido llega como HttpRequestException con InnerException
/// AuthenticationException; el timeout de HttpClient.Timeout NO llega envuelto en
/// HttpRequestException — es un TaskCanceledException directo con InnerException TimeoutException,
/// distinto de una cancelación externa via CancellationToken (esa no tiene TimeoutException
/// adentro y se re-lanza tal cual, no se mapea a un resultado).
///
/// NUNCA se afloja ServerCertificateCustomValidationCallback ni equivalentes: un probador que
/// acepta cualquier certificado miente sobre el único punto que tiene que verificar.
/// </summary>
public sealed class ProbadorConexion : IProbadorConexion
{
    private readonly TimeSpan _timeout;

    /// <param name="timeoutOverride">
    /// Solo para tests: permite un timeout corto para no esperar 12s reales al verificar el
    /// camino de Timeout. En producción se usa sin argumentos (12s).
    /// </param>
    public ProbadorConexion(TimeSpan? timeoutOverride = null)
    {
        _timeout = timeoutOverride ?? TimeSpan.FromSeconds(12);
    }

    public async Task<ResultadoPruebaConexion> ProbarAsync(string baseUrl, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = _timeout };

        HttpResponseMessage respuesta;
        try
        {
            var url = baseUrl.TrimEnd('/') + "/";
            respuesta = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            if (ex.InnerException is AuthenticationException)
            {
                return ResultadoPruebaConexion.CertificadoInvalido;
            }

            if (ex.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound })
            {
                return ResultadoPruebaConexion.NoResuelveNombre;
            }

            // Conexión rechazada, host inalcanzable, o cualquier otra SocketException que no
            // sea resolución de nombre: hay ruta pero nadie contesta como se espera.
            return ResultadoPruebaConexion.NoHayConexion;
        }
        catch (TaskCanceledException ex)
        {
            if (ex.InnerException is TimeoutException)
            {
                // HttpClient.Timeout venció: verificado empíricamente, .NET envuelve el timeout
                // interno en TaskCanceledException con InnerException TimeoutException — no en
                // HttpRequestException. Distinto de una cancelación externa vía `ct` (no trae
                // TimeoutException adentro).
                return ResultadoPruebaConexion.Timeout;
            }

            // Cancelación real pedida por el llamador (su propio `ct`, no nuestro timeout
            // interno): no es un resultado nuestro para mapear, se propaga tal cual.
            throw;
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            // UriFormatException: baseUrl no es una URI válida. InvalidOperationException:
            // HttpClient.GetAsync la recibe como relativa (sin esquema/host) y no hay
            // BaseAddress configurado. En ambos casos es un dato de entrada inválido — no hay
            // conexión posible con eso, mismo bucket que "sin ruta/rechazada".
            return ResultadoPruebaConexion.NoHayConexion;
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
            {
                return ResultadoPruebaConexion.RespondeOtraCosa;
            }

            var contenido = await respuesta.Content.ReadAsStringAsync(ct);

            try
            {
                using var doc = JsonDocument.Parse(contenido);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.String &&
                    status.GetString() == "ok")
                {
                    return ResultadoPruebaConexion.Ok;
                }
            }
            catch (JsonException)
            {
                // Respondió pero el cuerpo no es JSON: es "otra cosa" respondiendo en ese puerto.
            }

            return ResultadoPruebaConexion.RespondeOtraCosa;
        }
    }
}
