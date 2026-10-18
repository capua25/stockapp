using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls;
using Xunit;

namespace StockApp.Presentation.UiTests;

public class ComponentesBasicosTests
{
    private static Window Montar(string contenido)
    {
        var xaml = $$"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:c="using:StockApp.Presentation.Controls"
                    Width="600" Height="300">
                {{contenido}}
            </Window>
            """;
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void EstadoVacio_SinDatos_YFalloDeCarga_SonDistinguibles()
    {
        // Hoy los dos casos se ven identicos: una grilla vacia. El usuario no sabe si tiene que
        // cargar datos o reintentar.
        var vacio = Montar("""<c:EstadoVacio Titulo="Sin movimientos" Mensaje="Todavia no registraste ninguno." />""")
            .GetVisualDescendants().OfType<EstadoVacio>().First();

        var error = Montar("""<c:EstadoVacio Titulo="No se pudo cargar" Mensaje="Revisa la conexion." EsError="True" />""")
            .GetVisualDescendants().OfType<EstadoVacio>().First();

        Assert.False(vacio.EsError);
        Assert.True(error.EsError);

        var colorVacio = Assert.IsType<SolidColorBrush>(
            vacio.GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color;
        var colorError = Assert.IsType<SolidColorBrush>(
            error.GetVisualDescendants().OfType<TextBlock>().First().Foreground).Color;

        Assert.NotEqual(colorVacio, colorError);
    }
}
