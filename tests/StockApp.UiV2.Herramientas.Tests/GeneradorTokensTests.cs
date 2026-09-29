using System.Xml.Linq;
using StockApp.UiV2.Herramientas.Tokens;

namespace StockApp.UiV2.Herramientas.Tests;

public class GeneradorTokensTests
{
    private static readonly DefinicionTokens Prueba = LectorTokens.Leer(JsonTokensDePrueba.Valido);

    [Fact]
    public void GenerarAxaml_Color_EmiteColorYBrushConPrefijo()
    {
        var axaml = GeneradorTokens.GenerarAxaml(Prueba);

        Assert.Contains("<Color x:Key=\"V2ColorAcento\">#1D4ED8</Color>", axaml);
        Assert.Contains("<SolidColorBrush x:Key=\"V2AcentoBrush\" Color=\"#1D4ED8\" />", axaml);
        Assert.Contains("<SolidColorBrush x:Key=\"V2TextoSobreAcentoBrush\" Color=\"#FFFFFF\" />", axaml);
    }

    [Fact]
    public void GenerarAxaml_MedidasYTipografia_EmitenElTipoCorrecto()
    {
        var axaml = GeneradorTokens.GenerarAxaml(Prueba);

        Assert.Contains("<CornerRadius x:Key=\"V2RadioControl\">8</CornerRadius>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2AltoControl\">36</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2AnchoSidebar\">240</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2Espacio8\">8</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2FuenteTituloPaginaTamano\">22</x:Double>", axaml);
        Assert.Contains("<FontWeight x:Key=\"V2FuenteTituloPaginaPeso\">600</FontWeight>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2FuenteTituloPaginaAltoLinea\">26</x:Double>", axaml);
        Assert.Contains("<x:Double x:Key=\"V2FuenteHeaderColumnaEspaciadoLetras\">0.6</x:Double>", axaml);
        Assert.DoesNotContain("V2FuenteHeaderColumnaAltoLinea", axaml);
    }

    [Fact]
    public void Sombra_CuantizaElAlfaIgualEnAxamlYEnCss()
    {
        // 0.05 * 255 = 12.75 -> 13 = 0x0D. El CSS usa 13/255 = 0.051 para que Chromium y
        // Skia pinten EXACTAMENTE el mismo alfa, no dos redondeos distintos de 0.05.
        Assert.Contains("<BoxShadows x:Key=\"V2SombraCard\">0 1 2 0 #0D000000</BoxShadows>", GeneradorTokens.GenerarAxaml(Prueba));
        Assert.Contains("--v2-sombra-card: 0px 1px 2px 0px rgba(0, 0, 0, 0.051);", GeneradorTokens.GenerarCss(Prueba));
    }

    [Fact]
    public void GenerarCss_UsaKebabCaseYUnidades()
    {
        var css = GeneradorTokens.GenerarCss(Prueba);

        Assert.Contains("--v2-color-texto-sobre-acento: #FFFFFF;", css);
        Assert.Contains("--v2-radio-control: 8px;", css);
        Assert.Contains("--v2-espacio-4: 4px;", css);
        Assert.Contains("--v2-fuente-titulo-pagina-alto-linea: 26px;", css);
        Assert.Contains("--v2-fuente-header-columna-espaciado-letras: 0.6px;", css);
        Assert.Contains("--v2-fuente-header-columna-transformacion: uppercase;", css);
        Assert.Contains("--v2-fuente-titulo-pagina-peso: 600;", css);
        // Sin altoLinea en el token: ceil(11 × 1.20996) = ceil(13.31) = 14, lo que mide Avalonia.
        Assert.Contains("--v2-fuente-header-columna-alto-linea: 14px;", css);
    }

    [Theory]
    [InlineData("HeaderGrilla", "header-grilla")]
    [InlineData("V2", "v2")]
    [InlineData("InsigniaExitoFondo", "insignia-exito-fondo")]
    public void AKebab_SeparaPalabras(string pascal, string kebab) =>
        Assert.Equal(kebab, GeneradorTokens.AKebab(pascal));

    [Fact]
    public void SinPrefijo_LasClavesQuedanSinV2()
    {
        // Fase 9: vaciar "prefijo" en tokens.json convierte los tokens v2 en los únicos.
        var sinPrefijo = Prueba with { Prefijo = "" };

        Assert.Contains("x:Key=\"AcentoBrush\"", GeneradorTokens.GenerarAxaml(sinPrefijo));
        Assert.Contains("--color-acento: #1D4ED8;", GeneradorTokens.GenerarCss(sinPrefijo));
    }

    [Fact]
    public void TokensReales_GeneranXmlBienFormadoSinDobleGuionEnLosComentarios()
    {
        // "--" dentro del TEXTO de un comentario XAML rompe el build con AVLN1001. Los
        // delimitadores <!-- y --> los escribe el propio generador (y contienen "--" ellos
        // mismos), así que un chequeo global sobre todo el string nunca podría pasar: se
        // valida solo el contenido de cada nodo de comentario, ya sin los delimitadores.
        var axaml = GeneradorTokens.GenerarAxaml(LectorTokens.LeerArchivo(RutasRepo.TokensJson));

        var documento = XDocument.Parse(axaml);
        foreach (var comentario in documento.DescendantNodes().OfType<XComment>())
        {
            Assert.DoesNotContain("--", comentario.Value);
        }

        Assert.Equal("ResourceDictionary", documento.Root!.Name.LocalName);
    }
}
