using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    // Foco de teclado: el lenguaje de foco v2 es el borde de acento de 1 px (TextBox y ComboBox :focus). La fila
    // lo dibuja en PART_Foco, encima del marco, para no mover el contenido; el FocusAdorner de Fluent (doble
    // rectángulo negro y blanco) se apaga para no dibujar dos anillos.
    private const string DosFilas = """
        <Button x:Name="F" Theme="{StaticResource V2TemaFilaPanel}">
            <TextBlock Text="Reparar bache en Av. 18 de Julio" Height="19" />
        </Button>
        <Button x:Name="G" Theme="{StaticResource V2TemaFilaPanel}">
            <TextBlock Text="Retirar poda zona Rambla" Height="19" />
        </Button>
        """;

    [AvaloniaFact]
    public void Fila_ConFocoDeTeclado_MuestraElAnilloDeAcentoSinMoverElContenido()
    {
        var ventana = Montar(DosFilas);
        try
        {
            var fila = Buscar<Button>(ventana, "F");
            var contenidoAntes = Parte<ContentPresenter>(fila, "PART_Contenido").Bounds;
            var anillo = Parte<Border>(fila, "PART_Foco");
            Assert.False(anillo.IsVisible);

            ventana.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(fila.IsFocused);
            Assert.True(anillo.IsVisible);
            Assert.Equal(new Thickness(1), anillo.BorderThickness);
            Assert.Equal(Hex("#1D4ED8"), ColorDe(anillo.BorderBrush));
            Assert.Equal(Parte<Border>(fila, "PART_Fila").Bounds.Size, anillo.Bounds.Size);
            Assert.Equal(contenidoAntes, Parte<ContentPresenter>(fila, "PART_Contenido").Bounds);
            Assert.Empty(AdornerLayer.GetAdornerLayer(fila)!.Children);

            ventana.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(Buscar<Button>(ventana, "G").IsFocused);
            Assert.False(anillo.IsVisible);
            Assert.True(Parte<Border>(Buscar<Button>(ventana, "G"), "PART_Foco").IsVisible);
        }
        finally
        {
            ventana.Close();
        }
    }

    [AvaloniaFact]
    public void Fila_ConFocoPorClick_NoMuestraElAnillo()
    {
        var ventana = Montar(Fila);
        try
        {
            var fila = Buscar<Button>(ventana, "F");

            Clic(ventana, fila);

            Assert.True(fila.IsFocused);
            Assert.False(Parte<Border>(fila, "PART_Foco").IsVisible);
        }
        finally
        {
            ventana.Close();
        }
    }
}
