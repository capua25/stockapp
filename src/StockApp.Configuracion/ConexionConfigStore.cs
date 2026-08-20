using System;
using System.IO;
using System.Text.Json;

namespace StockApp.Configuracion;

/// <summary>
/// Lee/escribe el archivo de conexión. El formato reusa la misma forma que
/// src/StockApp.Presentation/appsettings.json ("Api":"BaseUrl") a propósito: el
/// ConfigurationBuilder del desktop lo puede agregar como una fuente más de
/// Microsoft.Extensions.Configuration.Json sin ninguna lógica de fusión custom — solo
/// tiene que ganarle en orden de precedencia al appsettings.json de fábrica.
///
/// Se guarda la URL completa (no ip+puerto por separado): si el día de mañana hace falta
/// https, el formato ya lo soporta sin cambios.
/// </summary>
public static class ConexionConfigStore
{
    private static readonly JsonSerializerOptions OpcionesEscritura = new() { WriteIndented = true };

    /// <summary>Escribe <paramref name="baseUrl"/> en el archivo, creando la carpeta si falta.</summary>
    public static void Guardar(string baseUrl, string? rutaArchivo = null)
    {
        var ruta = rutaArchivo ?? RutaConexion.ObtenerRutaArchivo();

        var carpeta = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrEmpty(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        var documento = new ConexionDocumento(new ConexionDocumentoApi(baseUrl));
        var json = JsonSerializer.Serialize(documento, OpcionesEscritura);

        File.WriteAllText(ruta, json);
    }

    /// <summary>
    /// Devuelve la BaseUrl guardada, o null si el archivo no existe, no tiene la clave
    /// esperada, o está corrupto. Nunca lanza: es config best-effort, no un requisito duro
    /// para arrancar (el appsettings.json de fábrica sigue siendo la red de contención).
    /// </summary>
    public static string? Leer(string? rutaArchivo = null)
    {
        var ruta = rutaArchivo ?? RutaConexion.ObtenerRutaArchivo();

        if (!File.Exists(ruta))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(ruta);
            using var doc = JsonDocument.Parse(stream);

            if (doc.RootElement.TryGetProperty("Api", out var api) &&
                api.TryGetProperty("BaseUrl", out var baseUrl) &&
                baseUrl.ValueKind == JsonValueKind.String)
            {
                return baseUrl.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record ConexionDocumento(ConexionDocumentoApi Api);

    private sealed record ConexionDocumentoApi(string BaseUrl);
}
