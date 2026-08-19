using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián de accesibilidad de la paleta del sidebar "pizarra media". Fija los tres hechos que
/// justifican la eleccion de la paleta, incluido el limite duro: el verde de marca NO sirve como
/// texto sobre el sidebar. Esa restriccion es la que se olvida seis tandas mas tarde.
/// </summary>
public class SidebarContrasteTests
{
    private static Color ColorDe(string clave)
    {
        Assert.True(
            Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token de color '{clave}' no existe en Themes/Tokens.axaml.");
        return Assert.IsType<Color>(valor!);
    }

    [AvaloniaFact]
    public void TextoDelSidebar_SobreElFondo_SuperaAAA()
    {
        var ratio = Contraste.Ratio(ColorDe("ColorSidebarTexto"), ColorDe("ColorSidebar"));
        Assert.True(ratio >= 7.0, $"Texto de sidebar sobre fondo: {ratio:F2}:1, se esperaba AAA (>=7:1).");
    }

    [AvaloniaFact]
    public void TextoDelSidebar_SobreElItemActivo_SuperaAAA()
    {
        var ratio = Contraste.Ratio(Colors.White, ColorDe("ColorSidebarActivo"));
        Assert.True(ratio >= 7.0, $"Blanco sobre item activo: {ratio:F2}:1, se esperaba AAA (>=7:1).");
    }

    [AvaloniaFact]
    public void AcentoVerde_SirveComoGraficoPeroNOComoTexto()
    {
        // ESTA es la restriccion que hay que recordar: 4.44:1 pasa el umbral grafico (3:1) y NO
        // pasa el de texto (4.5:1). El verde va en la barra de acento y en los iconos del item
        // activo. Si algun dia se usa como color de TEXTO sobre el sidebar, este test se pone
        // rojo — y ese rojo es correcto, no hay que ajustarlo.
        var ratio = Contraste.Ratio(ColorDe("ColorSidebarAccent"), ColorDe("ColorSidebar"));

        Assert.True(ratio >= 3.0, $"El acento debe pasar el umbral grafico: {ratio:F2}:1 < 3:1.");
        Assert.True(ratio < 4.5,
            $"El acento da {ratio:F2}:1. Si ahora supera 4.5:1 alguien cambio la paleta: reevalua "
            + "si el verde puede usarse como texto y actualiza la restriccion de la spec.");
    }
}
