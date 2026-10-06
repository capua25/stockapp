using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>V2TemaFilaPanel: fila clickeable de un panel (Inicio, "Tareas que requieren atención").</summary>
public class FilaPanelV2Tests
{
    private const string Fila = """
        <Button x:Name="F" Theme="{StaticResource V2TemaFilaPanel}">
            <TextBlock Text="Reparar bache en Av. 18 de Julio" Height="19" />
        </Button>
        """;

    [AvaloniaFact]
    public void Fila_SeparadorArribaYPaddingVerticalDe8()
    {
        var fila = Buscar<Button>(Montar(Fila), "F");
        var marco = Parte<Border>(fila, "PART_Fila");

        Assert.Equal(new Thickness(0, 1, 0, 0), marco.BorderThickness);
        Assert.Equal(Hex("#F0EFED"), ColorDe(marco.BorderBrush));
        Assert.Equal(new Thickness(0, 8), marco.Padding);
        Assert.Equal(1 + 8 + 19 + 8, fila.Bounds.Height);
    }

    [AvaloniaFact]
    public void Fila_FondoTransparenteAunqueElEstiloGlobalDeButtonPongaSuperficie()
    {
        // Themes/Controls.axaml: Style Selector="Button" setea Background = SuperficieBrush. La plantilla no lo usa.
        var marco = Parte<Border>(Buscar<Button>(Montar(Fila), "F"), "PART_Fila");

        Assert.Equal(Colors.Transparent, ColorDe(marco.Background));
    }

    [AvaloniaFact]
    public void Fila_ConElMouseEncima_FondoHoverNeutro()
    {
        var ventana = Montar(Fila);
        var fila = Buscar<Button>(ventana, "F");
        var centro = fila.TranslatePoint(new Point(fila.Bounds.Width / 2, fila.Bounds.Height / 2), ventana)!.Value;

        ventana.MouseMove(centro);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(Hex("#F5F5F4"), ColorDe(Parte<Border>(fila, "PART_Fila").Background));
    }
}
