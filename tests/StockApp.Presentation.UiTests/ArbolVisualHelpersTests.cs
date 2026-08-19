using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// El helper existe porque IsVisible NO cae en cascada en Avalonia: un TextBox dentro de un
/// StackPanel con IsVisible=False sigue reportando su propio IsVisible=True. Todo test de gate
/// de permisos que use GetVisualDescendants necesita caminar la cadena de ancestros o va a dar
/// un falso verde. Estos tests fijan ese contrato.
/// </summary>
public class ArbolVisualHelpersTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                Width="400" Height="300">
            <StackPanel>
                <StackPanel x:Name="PanelVisible" IsVisible="True">
                    <TextBlock x:Name="HijoDePanelVisible" Text="uno" />
                </StackPanel>
                <StackPanel x:Name="PanelOculto" IsVisible="False">
                    <TextBlock x:Name="HijoDePanelOculto" Text="dos" />
                </StackPanel>
                <TextBlock x:Name="OcultoElMismo" Text="tres" IsVisible="False" />
            </StackPanel>
        </Window>
        """;

    private static TextBlock Buscar(Window w, string nombre)
        => w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == nombre);

    private static Window Montar()
    {
        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void EsVisibleEnArbol_HijoDeUnPanelVisible_EsVisible()
    {
        var window = Montar();
        Assert.True(ArbolVisual.EsVisibleEnArbol(Buscar(window, "HijoDePanelVisible")));
    }

    [AvaloniaFact]
    public void EsVisibleEnArbol_HijoDeUnPanelOculto_NoEsVisibleAunqueElHijoDigaQueSi()
    {
        var window = Montar();
        var hijo = Buscar(window, "HijoDePanelOculto");

        // El corazón del asunto: el hijo reporta IsVisible=True aunque su padre esté oculto.
        Assert.True(hijo.IsVisible);
        Assert.False(ArbolVisual.EsVisibleEnArbol(hijo));
    }

    [AvaloniaFact]
    public void EsVisibleEnArbol_ControlOcultoElMismo_NoEsVisible()
    {
        var window = Montar();
        Assert.False(ArbolVisual.EsVisibleEnArbol(Buscar(window, "OcultoElMismo")));
    }
}
