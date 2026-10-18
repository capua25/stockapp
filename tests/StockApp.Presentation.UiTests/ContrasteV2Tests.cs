using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Contraste WCAG 2.1 AA (4,5:1) de los pares de texto sobre fondo de los tokens v2 (spec §8.2). Los colores salen de
/// los recursos de la app (<c>Themes/V2/Tokens.axaml</c>, generado desde <c>docs/ui-v2/tokens.json</c>), no de
/// literales. Los dos pares que no llegan a AA están declarados como excepciones con piso igual al medido: no pueden
/// empeorar, y su ajuste de token queda como deuda de accesibilidad posterior a la entrega.
///
/// Es una caracterización: nace verde y no tiene una mutación que lo ponga en rojo sin tocar los tokens.
/// </summary>
public class ContrasteV2Tests(Xunit.ITestOutputHelper salida)
{
    private static Color ColorDe(string clave)
    {
        Assert.True(
            Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token '{clave}' no existe en Themes/V2/Tokens.axaml.");
        return Assert.IsType<SolidColorBrush>(valor!).Color;
    }

    [AvaloniaTheory]
    [InlineData("V2TextoPrimarioBrush", "V2SuperficieBrush")]
    [InlineData("V2TextoPrimarioBrush", "V2FondoBrush")]
    [InlineData("V2TextoSecundarioBrush", "V2SuperficieBrush")]
    [InlineData("V2TextoSecundarioBrush", "V2FondoBrush")]
    [InlineData("V2TextoEtiquetaBrush", "V2SuperficieBrush")]
    [InlineData("V2TextoEtiquetaBrush", "V2HeaderGrillaBrush")]
    [InlineData("V2TextoTerciarioBrush", "V2SuperficieBrush")]
    [InlineData("V2TextoTerciarioBrush", "V2HeaderGrillaBrush")]
    [InlineData("V2PeligroTextoBrush", "V2SuperficieBrush")]
    [InlineData("V2BordeErrorBrush", "V2SuperficieBrush")]
    [InlineData("V2AcentoBrush", "V2SuperficieBrush")]
    [InlineData("V2TextoSobreAcentoBrush", "V2AcentoBrush")]
    [InlineData("V2AcentoOscuroBrush", "V2AcentoSuaveBrush")]
    [InlineData("V2InsigniaNeutroTextoBrush", "V2InsigniaNeutroFondoBrush")]
    [InlineData("V2InsigniaAdvertenciaTextoBrush", "V2InsigniaAdvertenciaFondoBrush")]
    [InlineData("V2InsigniaPeligroTextoBrush", "V2InsigniaPeligroFondoBrush")]
    [InlineData("V2InsigniaExitoTextoBrush", "V2InsigniaExitoFondoBrush")]
    [InlineData("V2AlertaAdvertenciaTextoBrush", "V2AlertaAdvertenciaFondoBrush")]
    [InlineData("V2AlertaErrorTextoBrush", "V2AlertaErrorFondoBrush")]
    [InlineData("V2AlertaInfoTextoBrush", "V2AlertaInfoFondoBrush")]
    public void TextoSobreFondo_CumpleAA(string texto, string fondo)
    {
        var ratio = Contraste.Ratio(ColorDe(texto), ColorDe(fondo));
        salida.WriteLine($"{texto} sobre {fondo}: {ratio:F3}:1");

        Assert.True(ratio >= 4.5, $"{texto} sobre {fondo}: {ratio:F3}:1, AA pide 4,5:1.");
    }

    /// <summary>
    /// Excepciones declaradas. <c>V2TextoTerciarioBrush</c> sobre <c>V2FondoBrush</c> (4,473:1) es el texto del
    /// breadcrumb y del pie sobre el fondo de la app. <c>V2TextoGrupoBrush</c> sobre <c>V2SuperficieBrush</c>
    /// (2,522:1) es el de los encabezados de sección del sidebar, la versión y el KPI sin dato. Los tokens son los
    /// aprobados en el diseño: el ajuste queda como deuda (el de 2,52:1 es la primera deuda de accesibilidad). El
    /// piso es el valor medido: el contraste no puede empeorar.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("V2TextoTerciarioBrush", "V2FondoBrush", 4.47)]
    [InlineData("V2TextoGrupoBrush", "V2SuperficieBrush", 2.52)]
    public void ExcepcionDeclarada_NoEmpeora(string texto, string fondo, double piso)
    {
        var ratio = Contraste.Ratio(ColorDe(texto), ColorDe(fondo));
        salida.WriteLine($"{texto} sobre {fondo}: {ratio:F3}:1 (piso {piso:F2})");

        Assert.True(ratio >= piso, $"{texto} sobre {fondo}: {ratio:F3}:1 bajó del piso declarado {piso:F2}:1.");
    }
}
