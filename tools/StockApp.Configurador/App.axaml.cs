using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using StockApp.Configurador.Servicios;
using StockApp.Configurador.ViewModels;
using StockApp.Configurador.Views;

namespace StockApp.Configurador;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Sin contenedor DI: solo dos dependencias, se instancian a mano (Program 5).
            var viewModel = new ConfiguradorViewModel(new ProbadorConexion());

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
