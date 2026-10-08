using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StockApp.Presentation.Behaviors;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>El tooltip de una celda muestra el texto completo solo cuando la celda lo corta con elipsis.</summary>
public class TooltipSiSeCortaTests
{
    private static TextBlock Montar(string texto, double ancho)
    {
        var tb = new TextBlock { Text = texto, Width = ancho, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(tb, texto);
        TooltipSiSeCorta.SetActivo(tb, true);
        var ventana = new Window { Width = 600, Height = 200, Content = new UserControl { Classes = { "ui2" }, Content = tb } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return tb;
    }

    /// <summary>Dispara la apertura del tooltip de <paramref name="tb"/> y devuelve si algún manejador la canceló.</summary>
    internal static bool SeCancela(TextBlock tb)
    {
        var e = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent, tb);
        tb.RaiseEvent(e);
        return e.Cancel;
    }

    [AvaloniaFact]
    public void TextoQueEntra_NoAbreElTooltip()
    {
        var tb = Montar("Bolsa", 200);

        Assert.False(TooltipSiSeCorta.EstaCortado(tb));
        Assert.True(SeCancela(tb));
    }

    [AvaloniaFact]
    public void TextoCortado_AbreElTooltipConElTextoCompleto()
    {
        var tb = Montar("Metro cúbico puesto en obra con flete incluido", 120);

        Assert.True(TooltipSiSeCorta.EstaCortado(tb));
        Assert.False(SeCancela(tb));
        Assert.Equal("Metro cúbico puesto en obra con flete incluido", ToolTip.GetTip(tb));
    }

    [AvaloniaFact]
    public void SinElBehavior_ElTooltipNoSeCancela()
    {
        var tb = Montar("Bolsa", 200);
        TooltipSiSeCorta.SetActivo(tb, false);

        Assert.False(SeCancela(tb));
    }
}
