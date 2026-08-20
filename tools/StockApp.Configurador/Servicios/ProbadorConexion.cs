using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StockApp.Configurador.Servicios;

/// <summary>
/// Pega a GET / de la API (mismo endpoint anónimo que expone StockApp.Api, Program.cs:617) y
/// distingue los tres casos de la spec 2026-08-20. Timeout corto (4s, no los 10s del cliente
/// principal del desktop): acá "no responde" es el caso más común y el usuario está mirando
/// la ventana en vivo, esperando el resultado.
/// </summary>
public sealed class ProbadorConexion : IProbadorConexion
{
    private static readonly TimeSpan TimeoutPrueba = TimeSpan.FromSeconds(4);

    public async Task<ResultadoPruebaConexion> ProbarAsync(string baseUrl, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeoutPrueba };

        HttpResponseMessage respuesta;
        try
        {
            var url = baseUrl.TrimEnd('/') + "/";
            respuesta = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException)
        {
            return ResultadoPruebaConexion.NoResponde;
        }
        catch (TaskCanceledException)
        {
            // Timeout (HttpClient.Timeout) o cancelación externa: en ambos casos no se pudo
            // confirmar la conexión dentro del plazo.
            return ResultadoPruebaConexion.NoResponde;
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
        {
            // UriFormatException: baseUrl no es una URI válida. InvalidOperationException:
            // HttpClient.GetAsync la recibe como relativa (sin esquema/host) y no hay
            // BaseAddress configurado. En ambos casos es un dato de entrada inválido, mismo
            // resultado que "no responde" para el usuario.
            return ResultadoPruebaConexion.NoResponde;
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
