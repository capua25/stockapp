using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Diálogo modal que pide un texto libre obligatorio (módulo Documentos, spec 2026-08-11:
/// motivo de anulación/reapertura). "Aceptar" devuelve el texto tipeado (puede venir vacío
/// o en blanco: la validación de "no vacío" vive en el servicio, D8); "Cancelar" devuelve
/// null. Usá <see cref="Window.ShowDialog{TResult}"/> con TResult=string? para obtener el resultado.
/// </summary>
public partial class PedirTextoDialog : Window
{
    public PedirTextoDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Crea el diálogo con el título de ventana y el mensaje explicativo indicados.
    /// </summary>
    public PedirTextoDialog(string titulo, string mensaje) : this()
    {
        Title = titulo;
        MensajeText.Text = mensaje;
    }

    private void OnAceptarClick(object? sender, RoutedEventArgs e)
        => Close(TextoTextBox.Text);

    private void OnCancelarClick(object? sender, RoutedEventArgs e)
        => Close(null);
}
