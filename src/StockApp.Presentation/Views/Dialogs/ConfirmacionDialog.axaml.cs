using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Diálogo modal de confirmación. Muestra un mensaje y dos botones:
/// "Sí, continuar" (devuelve true) y "Cancelar" (devuelve false).
/// Usá <see cref="ShowDialog{T}"/> con T=bool para obtener el resultado.
/// </summary>
public partial class ConfirmacionDialog : Window
{
    public ConfirmacionDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Crea el diálogo con el mensaje indicado.
    /// </summary>
    public ConfirmacionDialog(string mensaje) : this()
    {
        MensajeText.Text = mensaje;
    }

    private void OnConfirmarClick(object? sender, RoutedEventArgs e)
        => Close(true);

    private void OnCancelarClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
