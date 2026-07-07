using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Diálogo modal informativo de una sola acción. Muestra un mensaje y un único botón
/// "Aceptar" que cierra el diálogo. Es el análogo de <see cref="ConfirmacionDialog"/> para
/// el caso en que no hay nada que confirmar/cancelar — solo informar (ej. un error de
/// dominio amigable al dar de baja una entidad de catálogo, o la red de seguridad global
/// de excepciones no manejadas, ver App.axaml.cs).
/// Usá <see cref="Window.ShowDialog(Window)"/> (no genérico: no hay resultado que devolver).
/// </summary>
public partial class MensajeDialog : Window
{
    public MensajeDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Crea el diálogo con el mensaje indicado.
    /// </summary>
    public MensajeDialog(string mensaje) : this()
    {
        MensajeText.Text = mensaje;
    }

    private void OnAceptarClick(object? sender, RoutedEventArgs e)
        => Close();
}
