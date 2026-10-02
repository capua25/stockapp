using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Tipografías del shell y del acceso (spec §2.3) y botón ghost de solo ícono.</summary>
public class TipografiaShellV2Tests
{
    [AvaloniaTheory]
    [InlineData("marca", 15, 700, "#1C1917")]
    [InlineData("marca-subtitulo", 12, 400, "#78716C")]
    [InlineData("header-seccion-sidebar", 11, 600, "#A8A29E")]
    [InlineData("titulo-login", 20, 600, "#1C1917")]
    [InlineData("avatar", 11, 600, "#44403C")]
    [InlineData("usuario-nombre", 12, 500, "#1C1917")]
    [InlineData("usuario-rol", 12, 400, "#78716C")]
    [InlineData("version", 12, 400, "#A8A29E")]
    public void ClaseTipografica_TamanoPesoYColorDeLaSpec(string clase, double tamano, int peso, string color)
    {
        var texto = Buscar<TextBlock>(Montar($"""<TextBlock x:Name="T" Classes="{clase}" Text="Texto" />"""), "T");

        Assert.Equal(tamano, texto.FontSize);
        Assert.Equal((FontWeight)peso, texto.FontWeight);
        Assert.Equal(Hex(color), ColorDe(texto.Foreground));
    }

    [AvaloniaFact]
    public void HeaderSeccionSidebar_LetterSpacing06()
    {
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="header-seccion-sidebar" Text="INVENTARIO" />"""), "T");

        Assert.Equal(0.6, texto.LetterSpacing);
    }

    [AvaloniaFact]
    public void ClaseFueraDeUi2_NoCambiaNada()
    {
        // Convivencia: "marca" no existe en el sistema viejo, fuera de .ui2 no aplica.
        var texto = Buscar<TextBlock>(Montar("""<TextBlock x:Name="T" Classes="marca" Text="Texto" />""", conScopeUi2: false), "T");

        Assert.NotEqual(15, texto.FontSize);
    }

    [AvaloniaFact]
    public void BotonGhostIcono_32x32SinPadding()
    {
        var boton = Buscar<Button>(Montar("""
            <Button x:Name="B" Classes="ghost icono"><i:Icon Value="mdi-logout" FontSize="16" /></Button>
            """), "B");

        Assert.Equal(32, boton.Bounds.Width);
        Assert.Equal(32, boton.Bounds.Height);
        Assert.Equal(new Avalonia.Thickness(0), boton.Padding);
    }
}
