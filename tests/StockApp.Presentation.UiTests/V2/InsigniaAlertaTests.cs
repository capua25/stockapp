using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class InsigniaAlertaTests
{
    [AvaloniaTheory]
    [InlineData("Neutro", "#F5F5F4", "#44403C")]
    [InlineData("Advertencia", "#FEF3C7", "#92400E")]
    [InlineData("Peligro", "#FEE2E2", "#991B1B")]
    [InlineData("Exito", "#DCFCE7", "#14532D")]
    public void Insignia_Variante_ColoresDeLaSpec(string variante, string fondo, string texto)
    {
        var insignia = Buscar<Insignia>(Montar($"""<v2:Insignia x:Name="I" Texto="Activo" Variante="{variante}" />"""), "I");

        Assert.Equal(Hex(fondo), ColorDe(Parte<Border>(insignia, "PART_Fondo").Background));
        Assert.Equal(Hex(texto), ColorDe(Parte<TextBlock>(insignia, "PART_Texto").Foreground));
    }

    [AvaloniaFact]
    public void Insignia_PillConPadding2x8Fuente12MediumAlineadaALaIzquierda()
    {
        var insignia = Buscar<Insignia>(Montar("""<v2:Insignia x:Name="I" Texto="Activo" />"""), "I");
        var fondo = Parte<Border>(insignia, "PART_Fondo");
        var texto = Parte<TextBlock>(insignia, "PART_Texto");

        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);
        Assert.Equal(new CornerRadius(999), fondo.CornerRadius);
        Assert.Equal(new Thickness(8, 2), fondo.Padding);
        Assert.Equal("Activo", texto.Text);
        Assert.Equal(12, texto.FontSize);
        Assert.Equal(FontWeight.Medium, texto.FontWeight);
        Assert.Equal(HorizontalAlignment.Left, insignia.HorizontalAlignment);
    }

    [AvaloniaFact]
    public void Insignia_FueraDeUi2_SeVeIgual()
    {
        // Un componente v2 no depende del scope: se ve bien aunque su vista todavía no migró.
        var insignia = Buscar<Insignia>(Montar("""<v2:Insignia x:Name="I" Texto="Stock negativo" Variante="Peligro" />""", conScopeUi2: false), "I");

        Assert.Equal(Hex("#FEE2E2"), ColorDe(Parte<Border>(insignia, "PART_Fondo").Background));
    }

    [AvaloniaTheory]
    [InlineData("Info", "#DBEAFE", "#DBEAFE", "#1E3A8A")]
    [InlineData("Advertencia", "#FEF3C7", "#FDE68A", "#92400E")]
    [InlineData("Error", "#FEE2E2", "#FCA5A5", "#991B1B")]
    public void Alerta_Variante_ColoresDeLaSpec(string variante, string fondo, string borde, string texto)
    {
        var alerta = Buscar<Alerta>(Montar($"""<v2:Alerta x:Name="A" Texto="No se pudo guardar." Variante="{variante}" />"""), "A");
        var marco = Parte<Border>(alerta, "PART_Marco");

        Assert.Equal(Hex(fondo), ColorDe(marco.Background));
        Assert.Equal(Hex(borde), ColorDe(marco.BorderBrush));
        Assert.Equal(Hex(texto), ColorDe(Parte<TextBlock>(alerta, "PART_Texto").Foreground));
    }

    [AvaloniaFact]
    public void Alerta_Radio8Padding16x12Borde1Fuente13ConAjuste()
    {
        var alerta = Buscar<Alerta>(Montar("""<v2:Alerta x:Name="A" Texto="Texto largo de la alerta" />"""), "A");
        var marco = Parte<Border>(alerta, "PART_Marco");
        var texto = Parte<TextBlock>(alerta, "PART_Texto");

        Assert.Equal(VarianteAlerta.Info, alerta.Variante);
        Assert.Equal(new CornerRadius(8), marco.CornerRadius);
        Assert.Equal(new Thickness(16, 12), marco.Padding);
        Assert.Equal(new Thickness(1), marco.BorderThickness);
        Assert.Equal(13, texto.FontSize);
        Assert.Equal(TextWrapping.Wrap, texto.TextWrapping);
    }

    [AvaloniaFact]
    public void Alerta_TextoEnvuelto_LasLineasVanACadaAltoDeLineaDeControl()
    {
        // El mockup usa line-height 16 (V2FuenteControlAltoLinea). Sin LineHeight en PART_Texto Avalonia usa el
        // natural (~15px) y la segunda línea queda 1px más arriba que en el mockup.
        var alerta = Buscar<Alerta>(
            Montar("""<v2:Alerta x:Name="A" Width="180" Texto="Modo de acceso limitado: la licencia está vencida." />"""), "A");
        var texto = Parte<TextBlock>(alerta, "PART_Texto");

        Assert.Equal(16, texto.LineHeight);
        Assert.True(texto.Bounds.Height >= 32, $"El texto no envolvió: alto {texto.Bounds.Height}.");
        Assert.Equal(0, texto.Bounds.Height % 16);
    }
}
