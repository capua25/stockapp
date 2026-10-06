using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Columnas iguales con los bordes donde los pone Chromium (cada borde redondeado por separado). UniformGrid
/// redondea el ANCHO y acumula: a 1366 la tercera tarjeta queda corrida 1 px respecto del mockup.
/// </summary>
public class GrillaUniformeV2Tests
{
    private static Rect[] Celdas(Window ventana) =>
        Buscar<GrillaUniformeV2>(ventana, "G").Children.Where(c => c.IsVisible).Select(c => c.Bounds).ToArray();

    [AvaloniaFact]
    public void TresColumnasFraccionarias_CadaBordeRedondeadoPorSeparado()
    {
        // (992 - 2*12) / 3 = 322,67: bordes en 0|322,67|334,67|657,33|669,33|992 → 0-323, 335-657, 669-992.
        var celdas = Celdas(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="992" Espaciado="12">
                <Border Height="40" /><Border Height="40" /><Border Height="40" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100));

        Assert.Equal(new Rect(0, 0, 323, 40), celdas[0]);
        Assert.Equal(new Rect(335, 0, 322, 40), celdas[1]);
        Assert.Equal(new Rect(669, 0, 323, 40), celdas[2]);
    }

    [AvaloniaFact]
    public void HijoInvisible_NoOcupaColumna()
    {
        // Gate: la tarjeta oculta no deja hueco; las visibles se reparten la fila.
        var celdas = Celdas(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="992" Espaciado="12">
                <Border Height="40" /><Border Height="40" IsVisible="False" /><Border Height="40" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100));

        Assert.Equal(new Rect(0, 0, 490, 40), celdas[0]);
        Assert.Equal(new Rect(502, 0, 490, 40), celdas[1]);
    }

    [AvaloniaFact]
    public void ColumnasFijas_CadaFilaMideSuItemMasAlto()
    {
        // Como grid-auto-rows: auto. Fila 1: 60 de alto; fila 2 arranca en 60 + 12.
        var celdas = Celdas(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="960" Columnas="3" Espaciado="12">
                <Border Height="40" VerticalAlignment="Top" /><Border Height="60" VerticalAlignment="Top" />
                <Border Height="50" VerticalAlignment="Top" /><Border Height="30" VerticalAlignment="Top" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100));

        // Pre-flight: con Height fijo y VerticalAlignment Stretch, Avalonia CENTRA el Border en el slot de 60
        // (quedaría en y 10); Top lo deja arriba, como CSS con un alto explícito. Las tarjetas reales no
        // fijan alto: se estiran a la fila.
        Assert.Equal(new Rect(0, 0, 312, 40), celdas[0]);
        Assert.Equal(new Rect(324, 0, 312, 60), celdas[1]);
        Assert.Equal(new Rect(0, 72, 312, 30), celdas[3]);
    }

    [AvaloniaFact]
    public void SinHijosVisibles_MideCeroYNoLanza()
    {
        var grilla = Buscar<GrillaUniformeV2>(Montar("""
            <v2:GrillaUniformeV2 x:Name="G" Width="992" Espaciado="12">
                <Border Height="40" IsVisible="False" />
            </v2:GrillaUniformeV2>
            """, ancho: 1100), "G");

        Assert.Equal(0, grilla.Bounds.Height);
    }
}
