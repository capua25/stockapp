using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Views;

public partial class BloqueoLicenciaView : UserControl
{
    public BloqueoLicenciaView()
    {
        InitializeComponent();

        // No hay un hook de navegación que dispare la carga de datos al mostrar la vista,
        // así que se cablea acá: cuando el ViewModel se asigna como DataContext, se carga
        // el código de máquina (igual que el resto de las Views del proyecto).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is BloqueoLicenciaViewModel vm)
                await vm.CargarEstadoAsync();
        };
    }

    // Interacción pura de View (sin lógica de negocio): copia el código de máquina al
    // portapapeles del sistema operativo y da feedback visual momentáneo en el botón.
    private async void CopiarCodigoMaquina_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BloqueoLicenciaViewModel vm || sender is not Button boton)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        await clipboard.SetValueAsync(DataFormat.Text, vm.CodigoMaquina);

        var contenidoOriginal = boton.Content;
        boton.Content = "Copiado ✓";
        await Task.Delay(1200);
        boton.Content = contenidoOriginal;
    }
}
