using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Al hacerse visible, el control pide que el ScrollViewer que lo contiene lo traiga a la vista. En Ingreso por factura
/// la card de adjuntos aparece al final de un formulario largo después de guardar: con el pie fijo y muchos renglones
/// quedaría debajo del pliegue y el usuario no sabría que tiene que adjuntar la factura.
/// </summary>
public static class TraerALaVista
{
    public static readonly AttachedProperty<bool> AlMostrarseProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("AlMostrarse");

    static TraerALaVista()
    {
        AlMostrarseProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.PropertyChanged -= AlCambiarPropiedad;
            if (e.GetNewValue<bool>())
                control.PropertyChanged += AlCambiarPropiedad;
        });
    }

    public static bool GetAlMostrarse(Control control) => control.GetValue(AlMostrarseProperty);

    public static void SetAlMostrarse(Control control, bool valor) => control.SetValue(AlMostrarseProperty, valor);

    private static void AlCambiarPropiedad(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || !e.GetNewValue<bool>() || sender is not Control control)
            return;
        // Después del layout: recién ahí el control tiene tamaño y el ScrollViewer sabe hasta dónde ir.
        Dispatcher.UIThread.Post(control.BringIntoView, DispatcherPriority.Background);
    }
}
