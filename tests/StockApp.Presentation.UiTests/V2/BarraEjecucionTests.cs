using Avalonia.Headless.XUnit;
using Avalonia.Media;
using StockApp.Presentation.Controls.V2;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

public class BarraEjecucionTests
{
    private static BarraEjecucion Montar(decimal porcentaje)
    {
        var ventana = MontajeV2.Montar($"""<v2:BarraEjecucion x:Name="Barra" Porcentaje="{porcentaje.ToString(System.Globalization.CultureInfo.InvariantCulture)}" HorizontalAlignment="Left" />""");
        return MontajeV2.Buscar<BarraEjecucion>(ventana, "Barra");
    }

    [AvaloniaTheory]
    [InlineData(47.3, 30)]
    [InlineData(94.26, 60)]
    [InlineData(100, 64)]
    [InlineData(114.3, 64)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void Geometria_64x6_YRellenoProporcionalConTopeEnCien(decimal porcentaje, double ancho)
    {
        var barra = Montar(porcentaje);

        Assert.Equal(64, barra.Bounds.Width);
        Assert.Equal(6, barra.Bounds.Height);
        Assert.Equal(ancho, barra.AnchoRelleno);
    }

    [AvaloniaFact]
    public void HastaCien_RellenoAcento_PistaBorde()
    {
        var barra = Montar(100m);

        Assert.Equal(MontajeV2.Hex("#1D4ED8"), MontajeV2.ColorDe(barra.RellenoVigente));
        Assert.Equal(MontajeV2.Hex("#E7E5E4"), MontajeV2.ColorDe(barra.Pista));
    }

    [AvaloniaFact]
    public void PorEncimaDeCien_RellenoPeligro()
    {
        var barra = Montar(114.3m);

        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(barra.RellenoVigente));
    }
}
