using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using System.Linq;

namespace StockApp.Presentation;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _serviceProvider = ConfigurarServicios();

        // Inicializa la BD (backup pre-migración + migrate) y arranca el backup periódico
        var initializer = _serviceProvider.GetRequiredService<DatabaseInitializer>();
        initializer.InicializarAsync().GetAwaiter().GetResult();

        var backupPeriodico = _serviceProvider.GetRequiredService<BackupPeriodicoService>();
        backupPeriodico.IniciarAsync().GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

            desktop.Exit += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigurarServicios()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();

        // AppDbContext: fábrica que resuelve la ruta desde IUserDataPathProvider
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
            var dataDir = pathProvider.GetDataDirectory();
            Directory.CreateDirectory(dataDir);
            options.UseSqlite($"DataSource={pathProvider.GetDatabasePath()}");
        });

        // BackupService: necesita rutas de archivos, no depende de EF
        services.AddSingleton<BackupService>(sp =>
        {
            var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
            return new BackupService(
                pathProvider.GetDatabasePath(),
                pathProvider.GetBackupsDirectory());
        });

        // DatabaseInitializer: singleton en el arranque (se usa una sola vez)
        services.AddTransient<DatabaseInitializer>();

        // BackupPeriodicoService: singleton — mantiene el timer vivo mientras corre la app
        services.AddSingleton<BackupPeriodicoService>();

        return services.BuildServiceProvider();
    }
}
