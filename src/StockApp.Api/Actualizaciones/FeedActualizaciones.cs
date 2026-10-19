using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace StockApp.Api.Actualizaciones;

/// <summary>
/// El feed de actualizaciones de Velopack, servido por la propia API en <see cref="Ruta"/>.
/// Anónimo (el cliente usa su propio HttpClient, sin token) y de solo lectura: sirve lo que el
/// operador publica en <see cref="ClaveDirectorio"/> (releases.{canal}.json y los .nupkg).
/// </summary>
public static class FeedActualizaciones
{
    public const string Ruta = "/updates";
    public const string ClaveDirectorio = "Actualizaciones:Directorio";
    public const string DirectorioPorDefecto = "/var/lib/stockapp/updates";

    // Explícito y mínimo a propósito: ServeUnknownFileTypes = false + este provider hacen que
    // cualquier otra extensión (.tmp de una publicación a medias, .exe) devuelva 404.
    private static readonly FileExtensionContentTypeProvider TiposDeContenido =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".json"] = "application/json",
            [".nupkg"] = "application/octet-stream",
        });

    public static void Montar(WebApplication app)
    {
        var configurado = app.Configuration[ClaveDirectorio];
        var directorio = string.IsNullOrWhiteSpace(configurado) ? DirectorioPorDefecto : configurado;

        try
        {
            Directory.CreateDirectory(directorio);
        }
        catch (Exception ex)
        {
            // Un feed que no se puede montar NO debe tumbar la API (A6): se avisa y la API
            // sigue sirviendo todo lo demás; /updates queda en 404.
            app.Logger.LogWarning(ex,
                "No se pudo preparar la carpeta del feed de actualizaciones '{Directorio}'; /updates no se sirve.",
                directorio);
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(directorio),
            RequestPath = Ruta,
            ServeUnknownFileTypes = false,
            ContentTypeProvider = TiposDeContenido,
            OnPrepareResponse = contexto =>
            {
                var extension = Path.GetExtension(contexto.File.Name);
                if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
                    contexto.Context.Response.Headers.CacheControl = "no-cache";
                else if (extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase))
                    contexto.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });
    }
}
