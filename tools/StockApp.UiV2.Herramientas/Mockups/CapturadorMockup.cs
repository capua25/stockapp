using Microsoft.Playwright;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Captura un mockup HTML a PNG de 1280x760 con Chromium de Playwright, escala 1.
/// --font-render-hinting=none: sin él, Chromium en Linux ensancha el texto por hinting
/// (medido: ~2% a 13px) y la comparación contra Skia se llena de ruido.
/// Antes de capturar fuerza la carga de TODAS las @font-face y falla si alguna no cargó o si el
/// contenido desborda el lienzo. El desborde se mide en dos lugares: el documento entero (una
/// ventana que se agranda a sí misma más allá de 1280x760) Y ".v2-ventana" puntualmente, porque
/// esa clase trae "overflow:hidden" de base.css — sin ese segundo chequeo, un hijo que se pasa
/// de la caja de ".v2-ventana" queda recortado en silencio en el PNG en vez de hacer fallar la
/// captura (overflow:hidden no propaga el desborde al documento).
/// </summary>
public static class CapturadorMockup
{
    public static readonly IReadOnlyList<string> ArgumentosChromium = ["--font-render-hinting=none"];

    private const string ScriptFuentes = """
        async () => {
          const caras = Array.from(document.fonts);
          await Promise.all(caras.map(f => f.load().catch(() => null)));
          return caras.map(f => f.family + " " + f.weight + ": " + f.status);
        }
        """;

    private const string ScriptDesbordes = """
        () => {
          const problemas = [];
          const raiz = document.documentElement;
          if (raiz.scrollWidth > raiz.clientWidth || raiz.scrollHeight > raiz.clientHeight) {
            problemas.push(`documento ${raiz.scrollWidth}x${raiz.scrollHeight} > ${raiz.clientWidth}x${raiz.clientHeight}`);
          }
          const ventana = document.querySelector(".v2-ventana");
          if (ventana && (ventana.scrollWidth > ventana.clientWidth || ventana.scrollHeight > ventana.clientHeight)) {
            problemas.push(`.v2-ventana ${ventana.scrollWidth}x${ventana.scrollHeight} > ${ventana.clientWidth}x${ventana.clientHeight}`);
          }
          return problemas;
        }
        """;

    public static async Task CapturarAsync(string rutaHtml, string rutaPng)
    {
        if (!File.Exists(rutaHtml))
        {
            throw new FileNotFoundException("No existe el mockup.", rutaHtml);
        }

        using var playwright = await Playwright.CreateAsync();
        IBrowser navegador;
        try
        {
            navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Args = ArgumentosChromium });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Chromium de Playwright no está instalado. Correr una vez: "
                + "dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador", ex);
        }

        await using (navegador)
        {
            var pagina = await navegador.NewPageAsync(new BrowserNewPageOptions
            {
                ViewportSize = new ViewportSize { Width = Lienzo.Ancho, Height = Lienzo.Alto },
                DeviceScaleFactor = 1,
            });
            await pagina.GotoAsync(new Uri(Path.GetFullPath(rutaHtml)).AbsoluteUri);

            var estados = await pagina.EvaluateAsync<string[]>(ScriptFuentes);
            if (estados.Length == 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(rutaHtml)} no declara ninguna @font-face: tiene que enlazar _base/tokens.css y _base/base.css.");
            }

            var sinCargar = estados.Where(e => !e.EndsWith(": loaded", StringComparison.Ordinal)).ToList();
            if (sinCargar.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(rutaHtml)}: fuentes sin cargar ({string.Join(", ", sinCargar)}).");
            }

            var problemas = await pagina.EvaluateAsync<string[]>(ScriptDesbordes);
            if (problemas.Length > 0)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(rutaHtml)} desborda el lienzo: {string.Join("; ", problemas)}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaPng))!);
            await pagina.ScreenshotAsync(new PageScreenshotOptions { Path = rutaPng });
        }
    }
}
