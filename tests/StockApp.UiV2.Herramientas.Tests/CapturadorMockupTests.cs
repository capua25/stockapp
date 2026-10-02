using SkiaSharp;
using StockApp.UiV2.Herramientas.Mockups;

namespace StockApp.UiV2.Herramientas.Tests;

/// <summary>
/// Review Focus 4: la captura tiene que fallar fuerte si el mockup no mide lo que debe
/// (fuente de fallback, contenido recortado), no devolver un PNG que "casi" coincide.
/// </summary>
public class CapturadorMockupTests
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("captura-").FullName;

    private static string Uri(string ruta) => new Uri(ruta).AbsoluteUri;

    private string Html(string cuerpo, string estilo = "", bool conBase = true)
    {
        var enlaces = conBase
            ? $"<link rel=\"stylesheet\" href=\"{Uri(RutasRepo.TokensCss)}\">"
              + $"<link rel=\"stylesheet\" href=\"{Uri(Path.Combine(RutasRepo.CarpetaBaseMockups, "base.css"))}\">"
            : "";
        var ruta = Path.Combine(_carpeta, $"{Guid.NewGuid():N}.html");
        File.WriteAllText(ruta,
            $"<!doctype html><html><head><meta charset=\"utf-8\">{enlaces}<style>{estilo}</style></head>"
            + $"<body><div class=\"v2-ventana\">{cuerpo}</div></body></html>");
        return ruta;
    }

    [Fact]
    public async Task BloqueDeColor_PngDe1280x760ConElColorEnSuPosicion()
    {
        var html = Html(
            "<div class=\"b\"></div><p class=\"t\">Texto con Inter</p>",
            ".b{position:absolute;left:100px;top:100px;width:200px;height:100px;background:var(--v2-color-acento)}"
            + ".t{position:absolute;left:100px;top:240px;margin:0}");
        var png = Path.Combine(_carpeta, "bloque.png");

        await CapturadorMockup.CapturarAsync(html, png);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1280, imagen.Width);
        Assert.Equal(760, imagen.Height);
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(150, 150));
        Assert.Equal(SKColor.Parse("#F7F7F5"), imagen.GetPixel(10, 10));
    }

    /// <summary>
    /// Task 5b (a/b): a 1366x768 el PNG mide eso y ".v2-ventana" se estira al tamaño pedido. El
    /// bloque cae entre x=1280 y x=1366: a 1280x760 ese mismo HTML desbordaría la ventana.
    /// </summary>
    [Fact]
    public async Task TamanoMunicipio_PngDe1366x768ConLaVentanaDeEseAncho()
    {
        var html = Html(
            "<div class=\"b\"></div>",
            ".b{position:absolute;left:1300px;top:740px;width:40px;height:20px;background:var(--v2-color-acento)}");
        var png = Path.Combine(_carpeta, "municipio.png");

        await CapturadorMockup.CapturarAsync(html, png, TamanoLienzo.Municipio);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1366, imagen.Width);
        Assert.Equal(768, imagen.Height);
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(1320, 750));
    }

    /// <summary>Task 5b (b): el chequeo de desborde mide contra 1366x768, no contra 1280x760.</summary>
    [Fact]
    public async Task TamanoMunicipio_ContenidoQueSaleDe1366_Lanza()
    {
        var html = Html("<div class=\"ancho\"></div>", ".ancho{width:1400px;height:10px}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png"), TamanoLienzo.Municipio));

        Assert.Contains("desborda", ex.Message);
        Assert.Contains("1366x768", ex.Message);
    }

    /// <summary>Task 5b (b): lo mismo en alto: 768 entra a 1366x768 y no a 1280x760.</summary>
    [Fact]
    public async Task TamanoMunicipio_ContenidoDe768DeAlto_NoLanzaYAlEstandarSi()
    {
        var html = Html("<div class=\"alto\"></div>", ".alto{width:10px;height:768px}");

        await CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "alto.png"), TamanoLienzo.Municipio);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "alto-estandar.png")));

        Assert.Contains("desborda", ex.Message);
    }

    [Fact]
    public async Task MockupSinFontFace_Lanza()
    {
        var html = Html("<p>Sin base.css</p>", conBase: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("@font-face", ex.Message);
    }

    [Fact]
    public async Task FuenteQueNoCarga_Lanza()
    {
        var html = Html(
            "<p class=\"t\">Texto</p>",
            "@font-face{font-family:\"Rota\";src:url(\"no-existe.ttf\")}.t{font-family:\"Rota\"}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("Rota", ex.Message);
    }

    [Fact]
    public async Task ContenidoQueDesborda_Lanza()
    {
        var html = Html("<div class=\"ancho\"></div>", "body{overflow:visible}.v2-ventana{width:1400px}.ancho{width:1400px;height:10px}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("desborda", ex.Message);
    }

    /// <summary>
    /// Decisión del controlador (H2): ".v2-ventana" trae "overflow:hidden" de base.css a
    /// 1280x760 fijos, así que un hijo que se pasa de esa caja queda recortado en silencio en
    /// el PNG (nunca se ve) en vez de hacer fallar la captura. A diferencia de
    /// "ContenidoQueDesborda_Lanza" (que agranda la ventana misma), acá la ventana se queda en
    /// su tamaño de siempre: solo el contenido de adentro se pasa. Si el chequeo mide nada más
    /// document.documentElement (como en el borrador original), este caso pasa inadvertido
    /// porque overflow:hidden no propaga el desborde al documento.
    /// </summary>
    [Fact]
    public async Task ContenidoQueDesbordaDentroDeLaVentana_Lanza()
    {
        var html = Html("<div class=\"ancho\"></div>", ".ancho{width:1400px;height:10px}");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CapturadorMockup.CapturarAsync(html, Path.Combine(_carpeta, "x.png")));

        Assert.Contains("desborda", ex.Message);
    }

    /// <summary>
    /// I1 (revisión final): si el proceso hereda un DISPLAY que no responde (WSLg caído, socket
    /// de /tmp/.X11-unix presente pero sin servidor detrás), Chromium headless se cuelga en
    /// ScreenshotAsync hasta el timeout de 30s con un TimeoutException que no menciona DISPLAY.
    /// El fix lanza Chromium con un entorno sin DISPLAY/WAYLAND_DISPLAY, así que la captura tiene
    /// que completarse igual aunque DISPLAY apunte a un servidor inexistente.
    /// </summary>
    [Fact]
    public async Task DisplayInexistente_NoCuelgaLaCaptura()
    {
        var displayOriginal = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", ":255");
        try
        {
            var html = Html("<p class=\"t\">Texto con Inter</p>");
            var png = Path.Combine(_carpeta, "display.png");

            await CapturadorMockup.CapturarAsync(html, png);

            Assert.True(File.Exists(png));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", displayOriginal);
        }
    }

    [Fact]
    public async Task HtmlInexistente_LanzaFileNotFound() =>
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => CapturadorMockup.CapturarAsync(Path.Combine(_carpeta, "no.html"), Path.Combine(_carpeta, "x.png")));

    /// <summary>
    /// Decisión del controlador (alfa): ComparadorImagenes solo mira R/G/B, así que un canal
    /// alfa distinto de 255 (transparencia) pasaría inadvertido en cualquier comparación. Los
    /// mockups y la ventana de la app tienen fondo sólido y la spec prohíbe transparencias
    /// apiladas (§6.1): la captura tiene que ser 100% opaca, verificado píxel por píxel.
    /// </summary>
    [Fact]
    public async Task Captura_TodosLosPixelesSonOpacos()
    {
        var html = Html(
            "<div class=\"b\"></div><p class=\"t\">Texto con Inter</p>",
            ".b{position:absolute;left:100px;top:100px;width:200px;height:100px;background:var(--v2-color-acento)}");
        var png = Path.Combine(_carpeta, "opaco.png");

        await CapturadorMockup.CapturarAsync(html, png);

        using var imagen = SKBitmap.Decode(png);
        Assert.All(imagen.Pixels, pixel => Assert.Equal((byte)255, pixel.Alpha));
    }
}
