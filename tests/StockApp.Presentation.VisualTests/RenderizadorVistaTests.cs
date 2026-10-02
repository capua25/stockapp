using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using StockApp.Presentation.Actualizaciones.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.Presentation.VisualTests;

public class RenderizadorVistaTests
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("render-").FullName;

    [AvaloniaFact]
    public void BorderDeColor_PngDe1280x760ConElColorEnSuPosicionYElFondoV2()
    {
        var bloque = new Border { Width = 200, Height = 100, Background = new SolidColorBrush(Color.Parse("#1D4ED8")) };
        Canvas.SetLeft(bloque, 100);
        Canvas.SetTop(bloque, 100);
        var png = Path.Combine(_carpeta, "bloque.png");

        RenderizadorVista.RenderizarAPng(new Canvas { Children = { bloque } }, null, png);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1280, imagen.Width);
        Assert.Equal(760, imagen.Height);
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(150, 150));
        Assert.Equal(SKColor.Parse("#F7F7F5"), imagen.GetPixel(10, 10));
    }

    /// <summary>
    /// Task 5b (a): con TamanoLienzo.Municipio el PNG mide 1366x768 y la vista ocupa todo ese
    /// lienzo (el bloque está entre x=1280 y x=1366, fuera del lienzo estándar).
    /// </summary>
    [AvaloniaFact]
    public void TamanoMunicipio_PngDe1366x768ConLaVistaEnTodoElLienzo()
    {
        var bloque = new Border { Width = 40, Height = 20, Background = new SolidColorBrush(Color.Parse("#1D4ED8")) };
        Canvas.SetLeft(bloque, 1300);
        Canvas.SetTop(bloque, 740);
        var png = Path.Combine(_carpeta, "municipio.png");

        RenderizadorVista.RenderizarAPng(new Canvas { Children = { bloque } }, null, png, tamano: TamanoLienzo.Municipio);

        using var imagen = SKBitmap.Decode(png);
        Assert.Equal(1366, imagen.Width);
        Assert.Equal(768, imagen.Height);
        Assert.Equal(SKColor.Parse("#1D4ED8"), imagen.GetPixel(1320, 750));
    }

    [AvaloniaFact]
    public void AsignaElViewModelComoDataContextDeLaVista()
    {
        var vista = new Canvas();
        var modelo = new object();

        RenderizadorVista.RenderizarAPng(vista, modelo, Path.Combine(_carpeta, "dc.png"));

        Assert.Same(modelo, vista.DataContext);
    }

    [AvaloniaFact]
    public void TextoSinFontFamilyExplicita_SeMideConInter()
    {
        // Review Focus F2: este test custodia el RESULTADO (el texto sin FontFamily explícita se
        // mide como Inter, no como el fallback del SO). Verificado por mutación: con
        // WithInterFont() + Typography.axaml (Setter de FontFamily sobre Window, UserControl)
        // presentes, cualquiera de los dos alcanza por sí solo para este resultado en este
        // proyecto (Skia real + UseHeadlessDrawing=false) — son redundantes entre sí acá. Solo
        // sacando AMBOS a la vez se mide con el fallback del SO (DejaVu Sans en WSL) y este test
        // cae en rojo.
        const string texto = "Productos del inventario 1234";
        var sinFamilia = new TextBlock { Text = texto, FontSize = 22 };
        var conInter = new TextBlock { Text = texto, FontSize = 22, FontFamily = new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter") };
        var conDejaVu = new TextBlock { Text = texto, FontSize = 22, FontFamily = new FontFamily("DejaVu Sans") };
        var ventana = new Window { Content = new StackPanel { Children = { sinFamilia, conInter, conDejaVu } } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(conInter.DesiredSize.Width, sinFamilia.DesiredSize.Width);
        Assert.NotEqual(conDejaVu.DesiredSize.Width, sinFamilia.DesiredSize.Width);
    }

    public static TheoryData<string> RolesSinAltoDeLineaFijo => new(
        LectorTokens.LeerArchivo(RutasRepo.TokensJson).Tipografia
            .Where(t => t.Estilo.AltoLinea is null)
            .Select(t => t.Nombre));

    [AvaloniaTheory]
    [MemberData(nameof(RolesSinAltoDeLineaFijo))]
    public void AltoDeLinea_DeCadaRol_EsElQueUsaElCssDeLosMockups(string rol)
    {
        // tokens.css emite ceil(tamaño × factorAltoLinea) como line-height de cada rol. Si la
        // Inter embebida cambia de versión o Avalonia cambia el redondeo, los textos apilados del
        // mockup derivan respecto de la app: este test lo detecta antes que la comparación.
        var tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);
        var estilo = tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        var texto = new TextBlock { Text = "Ág 123", FontSize = estilo.Tamano, FontWeight = (FontWeight)estilo.Peso };
        var ventana = new Window { Content = new StackPanel { Children = { texto } } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(GeneradorTokens.AltoLineaCss(estilo, tokens.FactorAltoLinea), texto.Bounds.Height);
    }

    public static TheoryData<string> RolesConAltoDeLineaFijo => new(
        LectorTokens.LeerArchivo(RutasRepo.TokensJson).Tipografia
            .Where(t => t.Estilo.AltoLinea is not null)
            .Select(t => t.Nombre));

    [AvaloniaTheory]
    [MemberData(nameof(RolesConAltoDeLineaFijo))]
    public void AltoDeLinea_DeCadaRolConAltoFijo_LlegaAlRecursoYAlRender(string rol)
    {
        // Hermana de la teoría de arriba: los roles con altoLinea explícito no se miden por su natural. El
        // recurso V2Fuente<Rol>AltoLinea tiene que existir (lo emite el generador) y, aplicado como LineHeight,
        // dar el alto que el CSS de los mockups usa.
        var tokens = LectorTokens.LeerArchivo(RutasRepo.TokensJson);
        var estilo = tokens.Tipografia.Single(t => t.Nombre == rol).Estilo;
        Assert.True(Avalonia.Application.Current!.TryGetResource($"V2Fuente{rol}AltoLinea", null, out var recurso),
            $"Falta el recurso V2Fuente{rol}AltoLinea.");
        var altoLinea = Assert.IsType<double>(recurso);
        var texto = new TextBlock { Text = "Ág 123", FontSize = estilo.Tamano, FontWeight = (FontWeight)estilo.Peso, LineHeight = altoLinea };
        var ventana = new Window { Content = new StackPanel { Children = { texto } } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(GeneradorTokens.AltoLineaCss(estilo, tokens.FactorAltoLinea), texto.Bounds.Height);
    }

    [AvaloniaFact]
    public void RenderizaUnaVistaReal_TodosLosPixelesDelPngSonOpacos()
    {
        // Ruling (ledger, Alfa): ComparadorImagenes ignora el canal alfa, así que un render que
        // se cuela con transparencia (por ejemplo un fondo mal resuelto) pasaría inadvertido en
        // la comparación de mockups. Este test renderiza una vista REAL (no una forma armada a
        // mano) con el pipeline Skia headless completo y verifica que el PNG resultante es 100%
        // opaco, tal como exige la spec para mockups y ventanas de la app.
        var png = Path.Combine(_carpeta, "vista-real.png");

        RenderizadorVista.RenderizarAPng(new ActualizacionBannerView(), null, png);

        using var imagen = SKBitmap.Decode(png);
        Assert.All(imagen.Pixels, pixel => Assert.Equal(255, pixel.Alpha));
    }
}
