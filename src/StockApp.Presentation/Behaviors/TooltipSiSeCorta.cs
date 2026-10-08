using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Tooltip solo cuando el texto se corta: sobre un TextBlock con TextTrimming y ToolTip.Tip, cancela la apertura
/// si ninguna línea quedó cortada. Se decide al abrir (no al cambiar el texto o el tamaño), así anda igual con
/// las celdas que la grilla recicla al scrollear u ordenar.
/// </summary>
public static class TooltipSiSeCorta
{
    public static readonly AttachedProperty<bool> ActivoProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, TextBlock, bool>("Activo");

    static TooltipSiSeCorta()
    {
        ActivoProperty.Changed.AddClassHandler<TextBlock>((tb, e) =>
        {
            ToolTip.RemoveToolTipOpeningHandler(tb, AlAbrir);
            if (e.GetNewValue<bool>())
                ToolTip.AddToolTipOpeningHandler(tb, AlAbrir);
        });
    }

    public static bool GetActivo(TextBlock tb) => tb.GetValue(ActivoProperty);

    public static void SetActivo(TextBlock tb, bool valor) => tb.SetValue(ActivoProperty, valor);

    /// <summary>True si alguna línea del texto quedó cortada con elipsis.</summary>
    internal static bool EstaCortado(TextBlock tb) => tb.TextLayout.TextLines.Any(l => l.HasCollapsed);

    private static void AlAbrir(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is TextBlock tb && !EstaCortado(tb))
            e.Cancel = true;
    }
}
