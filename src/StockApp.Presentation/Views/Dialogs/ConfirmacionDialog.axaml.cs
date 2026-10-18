using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Diálogo modal de confirmación. Muestra un mensaje y dos botones:
/// "Sí, continuar" (devuelve true) y "Cancelar" (devuelve false).
/// Usá <c>ShowDialog&lt;bool&gt;</c> para obtener el resultado.
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

    /// <summary>
    /// Crea el diálogo con el texto propio de cada botón (null deja el de siempre: "Cancelar" / "Sí, continuar").
    /// </summary>
    public ConfirmacionDialog(string mensaje, string? textoNegativo, string? textoAfirmativo) : this(mensaje)
    {
        if (textoNegativo is not null) CancelarButton.Content = textoNegativo;
        if (textoAfirmativo is not null) ConfirmarButton.Content = textoAfirmativo;
    }

    private void OnConfirmarClick(object? sender, RoutedEventArgs e)
        => Close(true);

    private void OnCancelarClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
