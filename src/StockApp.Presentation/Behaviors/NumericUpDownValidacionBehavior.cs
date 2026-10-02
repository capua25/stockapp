using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Integridad de datos (bug 2026-10-01) para <see cref="NumericUpDown"/>, global vía el style
/// NumericUpDown de Themes/Controls.axaml. Cómo se comporta el control solo (Avalonia 12.0.5,
/// verificado decompilando NumericUpDown):
///   - Un texto que no parsea ("20a5", "abc") o fuera de Minimum/Maximum hace tirar a
///     ConvertTextToValue dentro de SyncTextAndValueProperties, que se traga la excepción: Value
///     (y el ViewModel) se queda con el valor ANTERIOR y NO se marca ningún error.
///   - NumericUpDown no sobreescribe UpdateDataValidation: tampoco hay error por el binding.
///   - Al perder el foco, OnLostFocus llama CommitInput(forceTextUpdate: true), que vuelve a
///     escribir el texto del valor viejo: el campo "se corrige" solo, en silencio.
///
/// Este behavior:
///   1. Valida el texto con las mismas reglas que el control (ParsingNumberStyle, NumberFormat,
///      Minimum/Maximum) más dos propias: vacío es error salvo <see cref="PermitirVacioProperty"/>
///      (el ViewModel tiene un int, no un int?) y un número con decimales es error si el
///      FormatString es entero ("0", "N0"...).
///   2. Marca el error en <see cref="DataValidationErrors"/> del NumericUpDown: se ve en pantalla y
///      <see cref="ErroresDeEntradaBehavior"/> (Reportar, también global en el style) bloquea los
///      comandos de guardado del ViewModel.
///   3. Mientras el error está visible, deshace la reversión de OnLostFocus: vuelve a poner el
///      texto inválido (un handler de LostFocus de instancia corre DESPUÉS del de clase).
/// Si el ViewModel cambia el valor, ese valor reemplaza al texto inválido y el error se va.
/// </summary>
public static class NumericUpDownValidacionBehavior
{
    public static readonly AttachedProperty<bool> ValidarProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, NumericUpDown, bool>("Validar");

    /// <summary>True si el ViewModel acepta "sin valor" (propiedad int?): el campo vacío no es error.</summary>
    public static readonly AttachedProperty<bool> PermitirVacioProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, NumericUpDown, bool>("PermitirVacio");

    /// <summary>Qué se pide, para el mensaje: "Ingresá un {Que} válido, entre {Minimum} y {Maximum}.".</summary>
    public static readonly AttachedProperty<string> QueProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, NumericUpDown, string>("Que", "número");

    /// <summary>Texto inválido que el campo está mostrando (null si no hay error).</summary>
    private static readonly AttachedProperty<string?> TextoInvalidoProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, NumericUpDown, string?>("TextoInvalido");

    private static readonly AttachedProperty<bool> RestaurandoProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, NumericUpDown, bool>("Restaurando");

    private static readonly string[] FormatosEnteros = ["0", "D", "N0", "F0", "G0"];

    static NumericUpDownValidacionBehavior()
    {
        ValidarProperty.Changed.AddClassHandler<NumericUpDown>((nud, e) =>
        {
            nud.PropertyChanged -= AlCambiarPropiedad;
            nud.RemoveHandler(InputElement.LostFocusEvent, AlPerderFoco);
            if (!e.GetNewValue<bool>())
            {
                Limpiar(nud);
                return;
            }
            nud.PropertyChanged += AlCambiarPropiedad;
            nud.AddHandler(InputElement.LostFocusEvent, AlPerderFoco, RoutingStrategies.Bubble, handledEventsToo: true);
        });
    }

    public static bool GetValidar(NumericUpDown nud) => nud.GetValue(ValidarProperty);
    public static void SetValidar(NumericUpDown nud, bool value) => nud.SetValue(ValidarProperty, value);
    public static bool GetPermitirVacio(NumericUpDown nud) => nud.GetValue(PermitirVacioProperty);
    public static void SetPermitirVacio(NumericUpDown nud, bool value) => nud.SetValue(PermitirVacioProperty, value);
    public static string GetQue(NumericUpDown nud) => nud.GetValue(QueProperty);
    public static void SetQue(NumericUpDown nud, string value) => nud.SetValue(QueProperty, value);

    /// <summary>Mensaje de error para <paramref name="texto"/>, o null si es válido.</summary>
    public static string? Validar(NumericUpDown nud, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return GetPermitirVacio(nud) ? null : Mensaje(nud);

        if (!decimal.TryParse(texto, nud.ParsingNumberStyle, nud.NumberFormat, out var valor))
            return Mensaje(nud);

        if (valor < nud.Minimum || valor > nud.Maximum)
            return Mensaje(nud);

        if (FormatosEnteros.Contains(nud.FormatString, StringComparer.OrdinalIgnoreCase) && valor != decimal.Truncate(valor))
            return Mensaje(nud);

        return null;
    }

    private static string Mensaje(NumericUpDown nud)
    {
        var que = GetQue(nud);
        var cultura = CultureInfo.CurrentCulture;
        var minimo = nud.Minimum.ToString("0.##", cultura);
        if (nud.Maximum == decimal.MaxValue)
            return nud.Minimum == decimal.MinValue
                ? $"Ingresá un {que} válido."
                : $"Ingresá un {que} válido, desde {minimo}.";
        return $"Ingresá un {que} válido, entre {minimo} y {nud.Maximum.ToString("0.##", cultura)}.";
    }

    private static void AlCambiarPropiedad(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not NumericUpDown nud || nud.GetValue(RestaurandoProperty))
            return;

        if (e.Property == NumericUpDown.TextProperty)
        {
            // Un cambio de Text que el TextBox interno todavía no muestra no lo tipeó el usuario:
            // es la reversión de OnLostFocus (Value no cambió) o el eco de un Value nuevo (lo
            // resuelve la rama de ValueProperty). Con un error visible no se toca.
            var interno = TextBoxInterno(nud);
            var tipeado = interno is null || interno.Text == nud.Text;
            if (!tipeado && nud.GetValue(TextoInvalidoProperty) is not null)
                return;
            Evaluar(nud);
        }
        else if (e.Property == NumericUpDown.ValueProperty && nud.GetValue(TextoInvalidoProperty) is not null)
        {
            // Value solo cambia si un texto válido llegó al control o si el ViewModel lo cambió:
            // en los dos casos manda el valor nuevo.
            Evaluar(nud);
        }
    }

    private static void AlPerderFoco(object? sender, RoutedEventArgs e)
    {
        if (sender is not NumericUpDown nud || nud.GetValue(TextoInvalidoProperty) is not { } invalido)
            return;
        if (nud.Text == invalido && TextBoxInterno(nud)?.Text == invalido)
            return;

        nud.SetValue(RestaurandoProperty, true);
        try
        {
            nud.SetCurrentValue(NumericUpDown.TextProperty, invalido);
            if (TextBoxInterno(nud) is { } interno && interno.Text != invalido)
                interno.Text = invalido;
        }
        finally
        {
            nud.SetValue(RestaurandoProperty, false);
        }
    }

    private static void Evaluar(NumericUpDown nud)
    {
        var texto = TextBoxInterno(nud)?.Text ?? nud.Text;
        var error = Validar(nud, texto);
        if (error is null)
        {
            Limpiar(nud);
            return;
        }
        nud.SetValue(TextoInvalidoProperty, texto);
        DataValidationErrors.SetErrors(nud, new object[] { error });
    }

    private static void Limpiar(NumericUpDown nud)
    {
        if (nud.GetValue(TextoInvalidoProperty) is null && !DataValidationErrors.GetHasErrors(nud))
            return;
        nud.ClearValue(TextoInvalidoProperty);
        DataValidationErrors.ClearErrors(nud);
    }

    private static TextBox? TextBoxInterno(NumericUpDown nud)
        => nud.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.TemplatedParent == nud);
}
