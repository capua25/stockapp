using Avalonia;
using Avalonia.Controls;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Attached behavior que fuerza el foco de teclado sobre el control decorado cuando la propiedad
/// bindeada del ViewModel pasa a True. Usado en "Ingreso de stock por factura" (zona de carga de
/// renglones) para devolver el foco al ComboBox de producto apenas se agrega un artículo -- la
/// pantalla existe para cargar N artículos rápido, y la secuencia tiene que ser repetible sin
/// tocar el mouse.
///
/// El binding debe ser TwoWay (<c>Mode=TwoWay</c> en el .axaml): el behavior resetea la propiedad
/// attached a False después de enfocar, y ese reset se propaga de vuelta al ViewModel para que un
/// futuro <c>True</c> vuelva a disparar el cambio (si se dejara OneWay, setear True dos veces
/// seguidas sin que nadie la vuelva a False en el medio no dispararía el segundo foco).
/// Mismo patrón que <see cref="CalendarDatePickerFechaBehavior"/> (attached property +
/// static class handler).
/// </summary>
public static class FocoBehavior
{
    public static readonly AttachedProperty<bool> SolicitarFocoProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("SolicitarFoco");

    static FocoBehavior()
    {
        SolicitarFocoProperty.Changed.AddClassHandler<Control>(OnSolicitarFocoChanged);
    }

    public static bool GetSolicitarFoco(Control control) => control.GetValue(SolicitarFocoProperty);

    public static void SetSolicitarFoco(Control control, bool value) => control.SetValue(SolicitarFocoProperty, value);

    private static void OnSolicitarFocoChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            control.Focus();
            control.SetValue(SolicitarFocoProperty, false);
        }
    }
}
