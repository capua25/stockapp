using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Monta XAML dentro de una vista con (o sin) el scope .ui2, con el TestApp real. Los prefijos
/// v2 e i se declaran solo si el contenido los usa (la Task 10 corre antes de que exista el
/// namespace Controls.V2).
/// </summary>
internal static class MontajeV2
{
    public static Window Montar(string contenido, bool conScopeUi2 = true, double ancho = 800, double alto = 600)
    {
        var xmlnsV2 = contenido.Contains("v2:") ? " xmlns:v2=\"using:StockApp.Presentation.Controls.V2\"" : "";
        var xmlnsI = contenido.Contains("i:") ? " xmlns:i=\"https://github.com/projektanker/icons.avalonia\"" : "";
        var clases = conScopeUi2 ? " Classes=\"ui2\"" : "";
        var xaml = $"""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"{xmlnsV2}{xmlnsI}
                    Width="{ancho}" Height="{alto}">
                <UserControl{clases}>
                    <StackPanel Margin="16" Spacing="8">
                        {contenido}
                    </StackPanel>
                </UserControl>
            </Window>
            """;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(xaml, typeof(TestApp).Assembly);
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return ventana;
    }

    public static T Buscar<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    public static T Parte<T>(Control control, string nombre) where T : Control =>
        control.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    public static Color ColorDe(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    public static Color Hex(string hex) => Color.Parse(hex);

    /// <summary>Click real de puntero en el centro del control (dispara Command igual que un usuario).</summary>
    public static void Clic(Window ventana, Control control)
    {
        var centro = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseDown(centro, MouseButton.Left);
        ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
