using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using StockApp.Domain.Formato;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Confirmación de los cambios de precio de costo antes de guardar una factura. Una fila por cambio: tilde con el
/// nombre del producto y los dos precios en es-UY. "Confirmar y guardar" devuelve los tildes en el orden recibido;
/// "Cancelar", Escape o cerrar la ventana devuelven null. Usá <see cref="Window.ShowDialog{TResult}"/> con
/// TResult=IReadOnlyList&lt;bool&gt;?.
/// </summary>
public partial class ConfirmacionPreciosDialog : Window
{
    public ConfirmacionPreciosDialog()
    {
        InitializeComponent();
    }

    public ConfirmacionPreciosDialog(IReadOnlyList<ItemConfirmacionPrecioVm> cambios) : this()
    {
        for (var i = 0; i < cambios.Count; i++)
            AgregarFila(i + 1, cambios[i]);

        // Enter sobre un tilde: Avalonia lo trata como un click (cambia el tilde y marca el evento), así que el botón
        // por defecto nunca se entera. Se intercepta en túnel y se confirma; el tilde se cambia con Espacio.
        AddHandler(KeyDownEvent, EnterEnUnTildeConfirma, RoutingStrategies.Tunnel);
        Opened += (_, _) => ConfirmarButton.Focus();
    }

    private void AgregarFila(int fila, ItemConfirmacionPrecioVm cambio)
    {
        Filas.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var tilde = new CheckBox
        {
            IsChecked = cambio.Confirmado,
            Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = cambio.ProductoNombre, TextWrapping = TextWrapping.Wrap },
        };
        Grid.SetRow(tilde, fila);
        Filas.Children.Add(tilde);
        Filas.Children.Add(Precio(cambio.PrecioActual, fila, 2));
        Filas.Children.Add(Precio(cambio.PrecioNuevo, fila, 4));
    }

    private static TextBlock Precio(decimal valor, int fila, int columna)
    {
        var texto = new TextBlock
        {
            Classes = { "precio" },
            Text = FormatoEsUy.Moneda(valor),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(texto, fila);
        Grid.SetColumn(texto, columna);
        return texto;
    }

    private IReadOnlyList<bool> TildesElegidos() =>
        Filas.Children.OfType<CheckBox>().Select(t => t.IsChecked == true).ToList();

    private void EnterEnUnTildeConfirma(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is not CheckBox)
            return;
        e.Handled = true;
        Close(TildesElegidos());
    }

    private void OnConfirmarClick(object? sender, RoutedEventArgs e) => Close(TildesElegidos());

    private void OnCancelarClick(object? sender, RoutedEventArgs e) => Close(null);
}
