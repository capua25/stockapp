using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Indicador de carga v2: texto secundario 13/16 centrado con 32 arriba y abajo, sin redondeo de layout.</summary>
public class EstadoCargandoTests
{
    [AvaloniaFact]
    public void Texto_SecundarioDeControlCentradoConMargen32()
    {
        var ventana = MontajeV2.Montar("""<v2:EstadoCargando x:Name="Indicador" Texto="Cargando productos…" />""", ancho: 800);
        var indicador = MontajeV2.Buscar<EstadoCargando>(ventana, "Indicador");
        var texto = MontajeV2.Parte<TextBlock>(indicador, "PART_Texto");

        Assert.Equal("Cargando productos…", texto.Text);
        Assert.Equal(13, texto.FontSize);
        Assert.Equal(16, texto.LineHeight);
        Assert.Equal(MontajeV2.Hex("#57534E"), MontajeV2.ColorDe(texto.Foreground));
        Assert.Equal(HorizontalAlignment.Center, texto.HorizontalAlignment);
        Assert.Equal(new Avalonia.Thickness(0, 32), texto.Margin);
        Assert.Equal(16, texto.Bounds.Height);
        var centro = texto.TranslatePoint(new Avalonia.Point(texto.Bounds.Width / 2, 0), indicador)!.Value.X;
        Assert.Equal(indicador.Bounds.Width / 2, centro, 0.5);
    }

    [AvaloniaFact]
    public void SinRedondeoDeLayout_PorqueElTextoCentradoTieneAnchoFraccionario()
    {
        var ventana = MontajeV2.Montar("""<v2:EstadoCargando x:Name="Indicador" Texto="Cargando productos…" />""");

        Assert.False(MontajeV2.Buscar<EstadoCargando>(ventana, "Indicador").UseLayoutRounding);
    }
}
