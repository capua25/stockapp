using StockApp.UiV2.Herramientas.Mockups;
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas;

/// <summary>
/// Comandos de la herramienta de UI v2. Códigos de salida: 0 ok, 1 el comando falló,
/// 2 uso incorrecto. Cada comando se documenta en docs/ui-v2/herramientas.md.
/// </summary>
public static class Cli
{
    public const string Uso = """
        Uso: dotnet run --project tools/StockApp.UiV2.Herramientas -- <comando>

        Comandos:
          tokens [--verificar]              Genera Themes/V2/Tokens.axaml y mockups/_base/tokens.css desde docs/ui-v2/tokens.json
          fuentes [--verificar]             Copia los TTF de Inter embebidos en Avalonia.Fonts.Inter a mockups/_base/fuentes
          icono-svg <clave-mdi>             Imprime el SVG inline de un ícono MDI (p. ej. mdi-package-variant)
          instalar-navegador                Instala el Chromium de Playwright (una vez por máquina)
          capturar-mockup <html> <png>      Captura un mockup HTML a PNG de 1280x760
          comparar-png <ref> <cand> <diff>  Compara dos PNG y escribe la imagen de diferencias
        """;

    /// <param name="rutaTokensJson">
    /// Ruta de tokens.json que usa el comando "tokens". Solo para tests: el default
    /// (null → RutasRepo.TokensJson) es el único valor que usa la CLI real.
    /// </param>
    public static int Ejecutar(string[] args, TextWriter salida, TextWriter error, string? rutaTokensJson = null)
    {
        try
        {
            switch (args)
            {
                case ["tokens"]:
                    return ComandoTokens(verificar: false, salida, error, rutaTokensJson ?? RutasRepo.TokensJson);
                case ["tokens", "--verificar"]:
                    return ComandoTokens(verificar: true, salida, error, rutaTokensJson ?? RutasRepo.TokensJson);
                case ["fuentes"]:
                    foreach (var ruta in ExtractorFuentes.Extraer(RutasRepo.CarpetaFuentes))
                    {
                        salida.WriteLine($"Escrito: {Path.GetRelativePath(RutasRepo.Raiz, ruta)}");
                    }

                    return 0;
                case ["fuentes", "--verificar"]:
                {
                    var diferencias = ExtractorFuentes.Diferencias(RutasRepo.CarpetaFuentes);
                    foreach (var ruta in diferencias)
                    {
                        error.WriteLine($"Distinta de la embebida: {Path.GetRelativePath(RutasRepo.Raiz, ruta)}");
                    }

                    return diferencias.Count == 0 ? 0 : 1;
                }

                case ["icono-svg", var clave]:
                    salida.WriteLine(ExportadorIconos.Svg(clave));
                    return 0;
                default:
                    error.WriteLine(Uso);
                    return 2;
            }
        }
        catch (TokensInvalidosException ex)
        {
            error.WriteLine($"tokens.json inválido: {ex.Message}");
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FileNotFoundException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static string Normalizar(string texto) => texto.Replace("\r\n", "\n");

    private static int ComandoTokens(bool verificar, TextWriter salida, TextWriter error, string rutaTokensJson)
    {
        var definicion = LectorTokens.LeerArchivo(rutaTokensJson);
        var esperados = new[]
        {
            (Ruta: RutasRepo.TokensAxaml, Contenido: GeneradorTokens.GenerarAxaml(definicion)),
            (Ruta: RutasRepo.TokensCss, Contenido: GeneradorTokens.GenerarCss(definicion)),
        };

        if (verificar)
        {
            var desactualizados = esperados
                .Where(e => !File.Exists(e.Ruta) || Normalizar(File.ReadAllText(e.Ruta)) != e.Contenido)
                .Select(e => Path.GetRelativePath(RutasRepo.Raiz, e.Ruta))
                .ToList();
            foreach (var ruta in desactualizados)
            {
                error.WriteLine($"Desactualizado: {ruta}. Correr el comando tokens.");
            }

            if (desactualizados.Count == 0)
            {
                salida.WriteLine("Tokens generados al día.");
            }

            return desactualizados.Count == 0 ? 0 : 1;
        }

        foreach (var (ruta, contenido) in esperados)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            File.WriteAllText(ruta, contenido);
            salida.WriteLine($"Escrito: {Path.GetRelativePath(RutasRepo.Raiz, ruta)}");
        }

        return 0;
    }
}
