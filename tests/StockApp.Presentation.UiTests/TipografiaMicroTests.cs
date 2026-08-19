using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// La clase .micro es el nivel de etiqueta estructural que faltaba en la escala (titulo-vista 20 /
/// seccion 16 / body 14 / caption 12). La consumen los headers de las 21 grillas y los eyebrows
/// del header de vista: si se rompe, los 29 FontSize literales del codigo no tienen a donde mapear.
/// </summary>
public class TipografiaMicroTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="200">
            <TextBlock x:Name="Etiqueta" Classes="micro" Text="reportes" />
        </Window>
        """;

    private static TextBlock Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Etiqueta");
    }

    [AvaloniaFact]
    public void ClaseMicro_AplicaTamanio11()
    {
        Assert.Equal(11.0, Montar().FontSize);
    }

    [AvaloniaFact]
    public void ClaseMicro_UsaTextoTerciarioNoOpacidad()
    {
        // El punto de todo el ejercicio: el gris se DECLARA. Si aparece un Opacity aca, se
        // vuelve al problema original (contraste no medible, dependiente del fondo).
        var etiqueta = Montar();
        Assert.Equal(Color.Parse("#94A3B8"), Assert.IsType<SolidColorBrush>(etiqueta.Foreground).Color);
        Assert.Equal(1.0, etiqueta.Opacity);
    }

    [AvaloniaFact]
    public void ClaseMicro_TieneLetterSpacingParaQueRespireEnMayusculas()
    {
        Assert.True(Montar().LetterSpacing > 0, "Sin letter-spacing, una etiqueta de 11px en mayusculas se lee apretada.");
    }
}
