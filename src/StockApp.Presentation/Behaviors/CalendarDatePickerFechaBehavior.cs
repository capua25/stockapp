using Avalonia;
using Avalonia.Controls;
using StockApp.Presentation.Helpers;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Attached behavior para <see cref="CalendarDatePicker"/> que permite tipear
/// la fecha sin separadores (ej. "25091999") y la normaliza a "dd/MM/yyyy"
/// al salir del campo / Enter. Avalonia 12 parsea el texto con
/// <c>DateTime.ParseExact</c> en modo estricto, así que "25091999" no matchea
/// "dd/MM/yyyy" y dispara <see cref="CalendarDatePicker.DateValidationError"/>
/// dejando <c>SelectedDate</c> en null. Este behavior se suscribe a ese
/// evento y, si el texto son 8 dígitos válidos, setea <c>SelectedDate</c>
/// directamente — el propio CalendarDatePicker se encarga de reformatear el
/// TextBox visible con el nuevo valor.
/// Se activa seteando la attached property <c>NormalizarFechaTipeada="True"</c>
/// en el XAML del CalendarDatePicker; una sola implementación reutilizada en
/// los 6 controles de los 3 reportes con fecha.
/// </summary>
public static class CalendarDatePickerFechaBehavior
{
    public static readonly AttachedProperty<bool> NormalizarFechaTipeadaProperty =
        AvaloniaProperty.RegisterAttached<CalendarDatePicker, CalendarDatePicker, bool>(
            "NormalizarFechaTipeada");

    static CalendarDatePickerFechaBehavior()
    {
        NormalizarFechaTipeadaProperty.Changed.AddClassHandler<CalendarDatePicker>(OnNormalizarFechaTipeadaChanged);
    }

    public static bool GetNormalizarFechaTipeada(CalendarDatePicker picker)
        => picker.GetValue(NormalizarFechaTipeadaProperty);

    public static void SetNormalizarFechaTipeada(CalendarDatePicker picker, bool value)
        => picker.SetValue(NormalizarFechaTipeadaProperty, value);

    private static void OnNormalizarFechaTipeadaChanged(
        CalendarDatePicker picker, AvaloniaPropertyChangedEventArgs e)
    {
        // Evitamos suscripciones duplicadas si la property cambia más de una vez.
        picker.DateValidationError -= OnDateValidationError;

        if (e.GetNewValue<bool>())
            picker.DateValidationError += OnDateValidationError;
    }

    private static void OnDateValidationError(
        object? sender, CalendarDatePickerDateValidationErrorEventArgs e)
    {
        if (sender is CalendarDatePicker picker
            && NormalizadorFechaHelper.TryNormalizarFecha(e.Text, out var fecha))
        {
            picker.SelectedDate = fecha;
        }
    }
}
