using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián de CONTRASTE (no de valor) de la escala de grises de texto. Hasta este cambio solo
/// existía TokensDisenioTests.TextoTerciarioBrush_EsElGrisDeLaSpec, que fija el hex pero nunca
/// verificó si ese hex era legible: #94A3B8 daba 2.56:1 sobre blanco, muy por debajo del piso AA
/// de 4.5:1, y vivió así porque un test de valor solo avisa si el color CAMBIA, nunca si el color
/// está BIEN. ColorTextoTerciario se usa en DataGridColumnHeader (todo encabezado de columna de
/// la app) y en la clase .micro del design system (TipografiaMicroTests).
///
/// La escala completa se ensanchó (no solo el terciario) para preservar el orden jerárquico:
/// primario &lt; secundario &lt; terciario en luminancia (más oscuro a más claro). El techo de
/// luminancia relativa para pasar 4.5:1 sobre ColorFondo (#F8FAFC) es 0.17301; #64748B da 0.17064,
/// 1.4% de margen. Cualquier gris perceptiblemente más claro falla AA sobre el fondo de ventana.
/// </summary>
public class EscalaTextoContrasteTests
{
    private static Color ColorDe(string clave)
    {
        Assert.True(
            Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token de color '{clave}' no existe en Themes/Tokens.axaml.");
        return Assert.IsType<Color>(valor!);
    }

    [AvaloniaTheory]
    [InlineData("ColorTextoPrimario", "ColorSuperficie")]
    [InlineData("ColorTextoPrimario", "ColorFondo")]
    [InlineData("ColorTextoSecundario", "ColorSuperficie")]
    [InlineData("ColorTextoSecundario", "ColorFondo")]
    [InlineData("ColorTextoTerciario", "ColorSuperficie")]
    [InlineData("ColorTextoTerciario", "ColorFondo")]
    public void EscalaDeTexto_CumpleAA_ContraLosDosFondosReales(string claveTexto, string claveFondo)
    {
        var ratio = Contraste.Ratio(ColorDe(claveTexto), ColorDe(claveFondo));
        Assert.True(ratio >= 4.5,
            $"{claveTexto} sobre {claveFondo} da {ratio:F2}:1, por debajo del piso AA de 4.5:1.");
    }

    [AvaloniaFact]
    public void EscalaDeTexto_MantieneElOrdenJerarquico_PrimarioSecundarioTerciario()
    {
        // El test que habría evitado la trampa de la jerarquía invertida: si alguien "arregla" el
        // terciario solo (ej. lo sube a #475569, el mismo valor que el secundario nuevo) sin tocar
        // el resto de la escala, el terciario queda MAS OSCURO que el secundario y la jerarquía de
        // 3 niveles se invierte. El orden relativo es parte del contrato, no un detalle de cada
        // token por separado.
        var luminanciaPrimario = Contraste.Luminancia(ColorDe("ColorTextoPrimario"));
        var luminanciaSecundario = Contraste.Luminancia(ColorDe("ColorTextoSecundario"));
        var luminanciaTerciario = Contraste.Luminancia(ColorDe("ColorTextoTerciario"));

        Assert.True(luminanciaPrimario < luminanciaSecundario,
            $"ColorTextoPrimario (luminancia {luminanciaPrimario:F5}) debe ser mas oscuro que "
            + $"ColorTextoSecundario (luminancia {luminanciaSecundario:F5}).");
        Assert.True(luminanciaSecundario < luminanciaTerciario,
            $"ColorTextoSecundario (luminancia {luminanciaSecundario:F5}) debe ser mas oscuro que "
            + $"ColorTextoTerciario (luminancia {luminanciaTerciario:F5}).");
    }
}
