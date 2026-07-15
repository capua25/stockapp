using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Views;

public partial class ResetAdminView : UserControl
{
    public ResetAdminView()
    {
        InitializeComponent();
    }

    // Interacción pura de View (sin lógica de negocio): copia el código de máquina y el
    // desafío al portapapeles del sistema operativo, con feedback visual momentáneo.
    private async void CopiarCodigoMaquina_Click(object? sender, RoutedEventArgs e) =>
        await CopiarAlPortapapelesAsync(sender, vm => vm.CodigoMaquina);

    private async void CopiarDesafio_Click(object? sender, RoutedEventArgs e) =>
        await CopiarAlPortapapelesAsync(sender, vm => vm.Desafio);

    private async Task CopiarAlPortapapelesAsync(object? sender, Func<ResetAdminViewModel, string> obtenerTexto)
    {
        if (DataContext is not ResetAdminViewModel vm || sender is not Button boton)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetValueAsync(DataFormat.Text, obtenerTexto(vm));

        var contenidoOriginal = boton.Content;
        boton.Content = "Copiado ✓";
        await Task.Delay(1200);
        boton.Content = contenidoOriginal;
    }
}
