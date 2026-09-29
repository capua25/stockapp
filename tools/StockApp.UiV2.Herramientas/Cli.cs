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

    public static int Ejecutar(string[] args, TextWriter salida, TextWriter error)
    {
        try
        {
            switch (args)
            {
                default:
                    error.WriteLine(Uso);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FileNotFoundException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }
}
