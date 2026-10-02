using Microsoft.Playwright;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Captura un mockup HTML a PNG con Chromium de Playwright, escala 1, al tamaño pedido
/// (<see cref="TamanoLienzo.Estandar"/> 1280x760 por defecto, o <see cref="TamanoLienzo.Municipio"/>
/// 1366x768). El mismo HTML sirve para los dos: el viewport sale del tamaño y ".v2-ventana" toma
/// su ancho y alto de las variables --v2-ventana-ancho/--v2-ventana-alto, que el capturador define
/// en :root antes de medir (base.css las usa con 1280px/760px como valor por defecto).
/// --font-render-hinting=none: sin él, Chromium en Linux ensancha el texto por hinting
/// (medido: ~2% a 13px) y la comparación contra Skia se llena de ruido.
/// Antes de capturar fuerza la carga de TODAS las @font-face y falla si alguna no cargó o si el
/// contenido desborda el lienzo. El desborde se mide en dos lugares: el documento entero (una
/// ventana que se agranda a sí misma más allá del tamaño pedido) Y ".v2-ventana" puntualmente, porque
/// esa clase trae "overflow:hidden" de base.css — sin ese segundo chequeo, un hijo que se pasa
/// de la caja de ".v2-ventana" queda recortado en silencio en el PNG en vez de hacer fallar la
/// captura (overflow:hidden no propaga el desborde al documento).
/// Chromium headless igual intenta abrir DISPLAY/WAYLAND_DISPLAY si el proceso los heredó
/// (para el compositor). En WSL, si WSLg quedó caído, el socket de /tmp/.X11-unix sigue
/// existiendo pero no responde: el launch no falla, pero ScreenshotAsync se cuelga hasta el
/// timeout de 30s con un TimeoutException que no menciona DISPLAY para nada. Por eso se lanza
/// con un entorno explícito sin esas dos variables (Env reemplaza process.env entero, no lo
/// mergea: hay que copiar el resto a mano).
/// </summary>
public static class CapturadorMockup
{
    public static readonly IReadOnlyList<string> ArgumentosChromium = ["--font-render-hinting=none"];

    private static readonly IReadOnlyList<string> VariablesDeDisplay = ["DISPLAY", "WAYLAND_DISPLAY"];

    /// <summary>
    /// Copia el entorno del proceso actual sin DISPLAY/WAYLAND_DISPLAY, para que Chromium
    /// headless no intente conectarse a un servidor gráfico que puede estar caído (ver
    /// comentario de la clase). BrowserTypeLaunchOptions.Env, si se especifica, reemplaza
    /// process.env por completo en vez de mergearlo.
    /// </summary>
    internal static Dictionary<string, string> EntornoSinDisplay() =>
        Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key is string clave && !VariablesDeDisplay.Contains(clave, StringComparer.Ordinal))
            .ToDictionary(e => (string)e.Key!, e => e.Value?.ToString() ?? string.Empty);

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

    /// <param name="tamano">Tamaño del lienzo; null es <see cref="TamanoLienzo.Estandar"/>.</param>
    /// <param name="ocultarSelectores">Elementos que NO se comparan por píxeles (visibility:hidden: el layout no se
    /// mueve); hoy solo la marca de agua del Login (P5). Un selector sin coincidencias lanza.</param>
    public static async Task CapturarAsync(string rutaHtml, string rutaPng, TamanoLienzo? tamano = null,
        IReadOnlyCollection<string>? ocultarSelectores = null)
    {
        var lienzo = tamano ?? TamanoLienzo.Estandar;
        if (!File.Exists(rutaHtml))
        {
            throw new FileNotFoundException("No existe el mockup.", rutaHtml);
        }

        using var playwright = await Playwright.CreateAsync();
        IBrowser navegador;
        try
        {
            navegador = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Args = ArgumentosChromium,
                Env = EntornoSinDisplay(),
            });
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
                ViewportSize = new ViewportSize { Width = lienzo.Ancho, Height = lienzo.Alto },
                DeviceScaleFactor = 1,
            });
            await pagina.GotoAsync(new Uri(Path.GetFullPath(rutaHtml)).AbsoluteUri);
            await pagina.AddStyleTagAsync(new PageAddStyleTagOptions
            {
                Content = $":root {{ --v2-ventana-ancho: {lienzo.Ancho}px; --v2-ventana-alto: {lienzo.Alto}px; }}",
            });

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
                    $"{Path.GetFileName(rutaHtml)} desborda el lienzo de {lienzo}: {string.Join("; ", problemas)}.");
            }

            if (ocultarSelectores is { Count: > 0 })
            {
                foreach (var selector in ocultarSelectores)
                {
                    // Un selector con typo compararía contra el mockup CON el elemento: falla fuerte.
                    if (await pagina.Locator(selector).CountAsync() == 0)
                    {
                        throw new InvalidOperationException(
                            $"{Path.GetFileName(rutaHtml)}: el selector a ocultar '{selector}' no coincide con ningún elemento.");
                    }
                }

                await pagina.AddStyleTagAsync(new PageAddStyleTagOptions
                {
                    Content = string.Join("\n", ocultarSelectores.Select(s => $"{s} {{ visibility: hidden !important; }}")),
                });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rutaPng))!);
            await pagina.ScreenshotAsync(new PageScreenshotOptions { Path = rutaPng });
        }
    }
}
