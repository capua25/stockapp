using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using StockApp.Presentation.Behaviors;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>La card de adjuntos aparece al final de un formulario largo con el pie fijo: tiene que quedar a la vista.</summary>
public class TraerALaVistaTests
{
    private static (Window Ventana, ScrollViewer Scroll, Border Final) Montar(bool conComportamiento)
    {
        var final = new Border { Height = 120, IsVisible = false };
        if (conComportamiento)
            TraerALaVista.SetAlMostrarse(final, true);
        var scroll = new ScrollViewer
        {
            Content = new StackPanel { Children = { new Border { Height = 2000 }, final } },
        };
        var ventana = new Window { Width = 400, Height = 300, Content = scroll };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return (ventana, scroll, final);
    }

    private static bool EntraEnLaVista(ScrollViewer scroll, Control control)
    {
        var arriba = control.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
        return arriba >= -0.5 && arriba + control.Bounds.Height <= scroll.Viewport.Height + 0.5;
    }

    [AvaloniaFact]
    public void AlMostrarse_ElScrollLoTraeALaVista()
    {
        var (_, scroll, final) = Montar(conComportamiento: true);

        final.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(EntraEnLaVista(scroll, final), $"Offset {scroll.Offset.Y}: la card quedó fuera de la vista.");
    }

    [AvaloniaFact]
    public void Detector_SinElComportamiento_QuedaFueraDeLaVista()
    {
        var (_, scroll, final) = Montar(conComportamiento: false);

        final.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(EntraEnLaVista(scroll, final));
    }
}
