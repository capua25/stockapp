using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Publica al ViewModel dueño de la vista si algún campo tiene un error de entrada que vive SOLO
/// en la View (bug de integridad 2026-10-01). Un <see cref="TextBox"/> con texto que el converter
/// rechaza (ej. "5.4", "Usá coma para los decimales") muestra el error, pero el binding nunca
/// escribe en el ViewModel, que conserva el valor ANTERIOR válido: sin este behavior, Guardar
/// seguía habilitado y guardaba en silencio el valor viejo.
///
/// Cómo funciona: cada control con <c>Reportar=True</c> (TODO <see cref="TextBox"/>, vía el style
/// global de Themes/Controls.axaml, igual que <c>ErrorValidacionConverter</c>) avisa cuando cambia
/// su <see cref="DataValidationErrors.HasErrorsProperty"/> o entra/sale del árbol visual. El dueño
/// es el primer ancestro (incluido el propio control) cuyo DataContext implementa
/// <see cref="IConErroresDeEntrada"/>; las filas de grilla (ObservableObject/ObservableValidator)
/// no lo implementan, así que una celda reporta al ViewModel de la pantalla. Se lleva el conjunto
/// de controles en error POR dueño y se publica <c>HayErroresDeEntrada = conjunto no vacío</c>.
/// El ViewModel lo consulta en el CanExecute de su comando de guardar.
///
/// Una vista nueva lo hereda sin tocar XAML; el ViewModel solo suma
/// <c>&amp;&amp; !HayErroresDeEntrada</c> a su CanExecute y notifica el comando en
/// <see cref="ViewModelBase"/>.<c>AlCambiarErroresDeEntrada</c>.
/// </summary>
public static class ErroresDeEntradaBehavior
{
    public static readonly AttachedProperty<bool> ReportarProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("Reportar");

    /// <summary>Dueño al que el control reportó su error (para poder retirarlo aunque cambie el árbol).</summary>
    private static readonly AttachedProperty<IConErroresDeEntrada?> DuenioProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, IConErroresDeEntrada?>("DuenioErroresDeEntrada");

    private static readonly ConditionalWeakTable<IConErroresDeEntrada, HashSet<Control>> CamposConError = new();

    static ErroresDeEntradaBehavior()
    {
        ReportarProperty.Changed.AddClassHandler<Control>(OnReportarChanged);
        DataValidationErrors.HasErrorsProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (GetReportar(control))
                Reevaluar(control);
        });
    }

    public static bool GetReportar(Control control) => control.GetValue(ReportarProperty);

    public static void SetReportar(Control control, bool value) => control.SetValue(ReportarProperty, value);

    private static void OnReportarChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.AttachedToVisualTree -= AlEntrarAlArbol;
        control.DetachedFromVisualTree -= AlSalirDelArbol;

        if (e.GetNewValue<bool>())
        {
            control.AttachedToVisualTree += AlEntrarAlArbol;
            control.DetachedFromVisualTree += AlSalirDelArbol;
            Reevaluar(control);
        }
        else
        {
            Retirar(control);
        }
    }

    private static void AlEntrarAlArbol(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Reevaluar(control);
    }

    /// <summary>Un campo que sale del árbol (celda que termina de editarse, fila quitada, vista
    /// que se cierra) deja de bloquear: su error ya no está en pantalla.</summary>
    private static void AlSalirDelArbol(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
            Retirar(control);
    }

    private static void Reevaluar(Control control)
    {
        var duenio = DataValidationErrors.GetHasErrors(control) && TopLevel.GetTopLevel(control) is not null
            ? BuscarDuenio(control)
            : null;

        var anterior = control.GetValue(DuenioProperty);
        if (ReferenceEquals(anterior, duenio))
            return;

        Retirar(control);
        if (duenio is null)
            return;

        control.SetValue(DuenioProperty, duenio);
        var campos = CamposConError.GetOrCreateValue(duenio);
        campos.Add(control);
        duenio.HayErroresDeEntrada = true;
    }

    private static void Retirar(Control control)
    {
        if (control.GetValue(DuenioProperty) is not { } duenio)
            return;

        control.ClearValue(DuenioProperty);
        if (CamposConError.TryGetValue(duenio, out var campos))
        {
            campos.Remove(control);
            duenio.HayErroresDeEntrada = campos.Count > 0;
        }
    }

    private static IConErroresDeEntrada? BuscarDuenio(Control control)
        => control.GetSelfAndVisualAncestors()
            .OfType<StyledElement>()
            .Select(e => e.DataContext)
            .OfType<IConErroresDeEntrada>()
            .FirstOrDefault();
}
