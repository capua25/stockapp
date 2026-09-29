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
        // Review Focus 4: WithInterFont() NO fija la familia default en Avalonia 12 (queda
        // DejaVu Sans en WSL). Inter llega por el Setter de Typography.axaml sobre Window. Si ese
        // include se pierde, todo el texto del render se mide con otra fuente.
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
