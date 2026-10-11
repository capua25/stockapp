using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Behaviors;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Alta rápida de un producto nuevo dentro de Ingreso por factura. "Confirmar" devuelve el
/// <see cref="ProductoNuevoDto"/> si están Código, Nombre y Unidad de medida (Categoría es opcional, como en el
/// servicio); si falta algo, no cierra y la alerta nombra lo que falta. "Cancelar", Escape o cerrar la ventana
/// devuelven null. Usá <see cref="Window.ShowDialog{TResult}"/> con TResult=ProductoNuevoDto?.
/// </summary>
public partial class AltaProductoRapidaDialog : Window
{
    public AltaProductoRapidaDialog()
    {
        InitializeComponent();
    }

    public AltaProductoRapidaDialog(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades) : this()
    {
        CategoriaCombo.ItemsSource = categorias;
        UnidadCombo.ItemsSource = unidades;
        Opened += (_, _) => CodigoTextBox.Focus();
        // Un botón IsDefault no alcanza con un ComboBox: cerrado con un valor se come el Enter para abrir el
        // desplegable. Con valor y cerrado, Enter confirma; vacío lo abre como siempre.
        var confirmar = new RelayCommand(Confirmar);
        EnterEjecutaComando.SetComando(UnidadCombo, confirmar);
        EnterEjecutaComando.SetComando(CategoriaCombo, confirmar);
    }

    /// <summary>"Falta completar: Código, Nombre y Unidad de medida." con los que falten, en el orden de los campos.</summary>
    internal static string TextoFaltantes(IReadOnlyList<string> faltantes) =>
        "Falta completar: "
        + (faltantes.Count == 1 ? faltantes[0] : string.Join(", ", faltantes.Take(faltantes.Count - 1)) + " y " + faltantes[^1])
        + ".";

    private List<string> Faltantes()
    {
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(CodigoTextBox.Text))
            faltantes.Add("Código");
        if (string.IsNullOrWhiteSpace(NombreTextBox.Text))
            faltantes.Add("Nombre");
        if (UnidadCombo.SelectedItem is not UnidadMedida)
            faltantes.Add("Unidad de medida");
        return faltantes;
    }

    private void OnConfirmarClick(object? sender, RoutedEventArgs e) => Confirmar();

    private void Confirmar()
    {
        var faltantes = Faltantes();
        if (faltantes.Count > 0)
        {
            AlertaFaltantes.Texto = TextoFaltantes(faltantes);
            AlertaFaltantes.IsVisible = true;
            return;
        }

        Close(new ProductoNuevoDto(CodigoTextBox.Text!, NombreTextBox.Text!,
            (CategoriaCombo.SelectedItem as Categoria)?.Id, ((UnidadMedida)UnidadCombo.SelectedItem!).Id));
    }

    private void OnCancelarClick(object? sender, RoutedEventArgs e) => Close(null);
}
