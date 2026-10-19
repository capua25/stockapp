using Avalonia.Headless.XUnit;
using StockApp.Configurador.Views;
using Xunit;

namespace StockApp.Configurador.UiTests;

/// <summary>
/// La ventana real del Configurador debe resolver su Window.Icon (avares://GestionMunicipal.Configurador/Assets/icono-app.ico).
/// Si el AvaloniaResource del csproj se pierde, el XAML compila pero el ícono de la barra de tareas queda vacío.
/// </summary>
public class IconoVentanaTests
{
    [AvaloniaFact]
    public void MainWindowReal_TieneWindowIconResuelto()
    {
        var window = new MainWindow();

        Assert.NotNull(window.Icon);
    }
}
