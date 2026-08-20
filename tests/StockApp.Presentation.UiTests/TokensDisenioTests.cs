using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Guardián del contrato de nombres de Themes/Tokens.axaml. Las 58 vistas consumen estos
/// recursos por clave con DynamicResource: si una clave se renombra o se borra, el binding NO
/// explota — queda sin resolver y el control se cae a su valor default, en silencio, igual que
/// un {Binding PuedeXxx} con typo. Este test convierte ese fallo silencioso en un rojo.
/// </summary>
public class TokensDisenioTests
{
    private static object Recurso(string clave)
    {
        Assert.True(
            Avalonia.Application.Current!.TryGetResource(clave, ThemeVariant.Light, out var valor),
            $"El token '{clave}' no existe en Themes/Tokens.axaml. Las vistas que lo consumen con "
            + "DynamicResource se van a caer a su valor default sin avisar.");
        return valor!;
    }

    [AvaloniaTheory]
    [InlineData("Espacio1", 4.0)]
    [InlineData("Espacio2", 8.0)]
    [InlineData("Espacio3", 12.0)]
    [InlineData("Espacio4", 16.0)]
    [InlineData("Espacio5", 24.0)]
    [InlineData("Espacio6", 32.0)]
    [InlineData("Espacio7", 48.0)]
    public void EscalaDeEspaciado_ExisteConElValorDeLaSpec(string clave, double esperado)
    {
        Assert.Equal(esperado, Assert.IsType<double>(Recurso(clave)));
    }

    [AvaloniaFact]
    public void MargenVista_Es24EnLosCuatroLados()
    {
        // Espacio5. Es el margen exterior estandar de TODA vista: hoy van de 16 a 40 segun el
        // archivo, y NuevaImportacionView.axaml (509 lineas) directamente no tiene ninguno.
        Assert.Equal(new Thickness(24), Assert.IsType<Thickness>(Recurso("MargenVista")));
    }

    [AvaloniaFact]
    public void PaddingCard_Es16EnLosCuatroLados()
    {
        Assert.Equal(new Thickness(16), Assert.IsType<Thickness>(Recurso("PaddingCard")));
    }

    [AvaloniaFact]
    public void PaddingCelda_Es12Horizontal8Vertical()
    {
        Assert.Equal(new Thickness(12, 8, 12, 8), Assert.IsType<Thickness>(Recurso("PaddingCelda")));
    }

    [AvaloniaFact]
    public void PaddingCompacto_Es8EnLosCuatroLados()
    {
        // Ruling B-2 de la Fase B: faltaba un Thickness de 8 para las barras de accion de
        // ~20 vistas; sin el, la tanda 5 se lo comio como Padding="8" literal puntual.
        Assert.Equal(new Thickness(8), Assert.IsType<Thickness>(Recurso("PaddingCompacto")));
    }

    [AvaloniaFact]
    public void PaddingHolgado_Es48EnLosCuatroLados()
    {
        // Ruling B-31 de la Fase B (tanda 11): respiro de las 3 cards P6 (Login/ResetAdmin/
        // BloqueoLicencia). Padding="40" estaba fuera de la escala (32 y 48, no 40).
        Assert.Equal(new Thickness(48), Assert.IsType<Thickness>(Recurso("PaddingHolgado")));
    }

    [AvaloniaTheory]
    [InlineData("RadioChico", 4.0)]
    [InlineData("RadioBase", 6.0)]
    [InlineData("RadioCard", 10.0)]
    public void EscalaDeRadios_ExisteConElValorDeLaSpec(string clave, double esperado)
    {
        var radio = Assert.IsType<CornerRadius>(Recurso(clave));
        Assert.Equal(esperado, radio.TopLeft);
        Assert.Equal(esperado, radio.BottomRight);
    }

    [AvaloniaTheory]
    [InlineData("SombraCard")]
    [InlineData("SombraElevada")]
    [InlineData("SombraModal")]
    public void EscalaDeSombras_ExisteYNoEstaVacia(string clave)
    {
        var sombras = Assert.IsType<BoxShadows>(Recurso(clave));
        Assert.True(sombras.Count > 0, $"'{clave}' existe pero no define ninguna sombra.");
    }

    [AvaloniaFact]
    public void TextoTerciarioBrush_EsElGrisDeLaSpec()
    {
        // Reemplaza los 60 usos de Opacity="0.5|0.6|0.7". El color se declara, no se atenua:
        // asi el contraste es medible y testeable en vez de depender de sobre que fondo cayo.
        var brush = Assert.IsType<SolidColorBrush>(Recurso("TextoTerciarioBrush"));
        Assert.Equal(Color.Parse("#94A3B8"), brush.Color);
    }

    [AvaloniaTheory]
    [InlineData("InfoSuaveBrush", "#E0F2FE")]
    [InlineData("WarningSuaveBrush", "#FEF3C7")]
    [InlineData("DangerSuaveBrush", "#FEE2E2")]
    public void FondosSuavesSemanticos_ExistenConElValorDeLaSpec(string clave, string hex)
    {
        // Ruling B-26 (Fase B, tanda 12): el unico "suave" que existia era BrandSuaveBrush
        // (#DCFCE7). Sin estos tres, BadgeEstado[Tono=Advertencia|Peligro|Info] se caia al gris
        // DeshabilitadoFondoBrush del selector default (Componentes.axaml:76-78).
        var brush = Assert.IsType<SolidColorBrush>(Recurso(clave));
        Assert.Equal(Color.Parse(hex), brush.Color);
    }

    [AvaloniaTheory]
    [InlineData("InfoSuaveBrush")]
    [InlineData("WarningSuaveBrush")]
    [InlineData("DangerSuaveBrush")]
    public void FondosSuavesSemanticos_SoportanTextoPrimarioConAA(string clave)
    {
        // Ruling B-26: sobre estos fondos el texto va en TextoPrimarioBrush, NUNCA en el color
        // semantico (que da 2.42-3.95:1 medido con el mismo calculo de Contraste.Ratio). Este
        // test fija esa regla en el banco de pruebas.
        var fondo = Assert.IsType<SolidColorBrush>(Recurso(clave)).Color;
        var texto = Assert.IsType<SolidColorBrush>(Recurso("TextoPrimarioBrush")).Color;
        Assert.True(Contraste.Ratio(texto, fondo) >= 4.5,
            $"TextoPrimarioBrush sobre {clave} da {Contraste.Ratio(texto, fondo):F2}:1, por debajo de AA.");
    }
}
