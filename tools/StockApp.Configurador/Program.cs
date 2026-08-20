using Avalonia;
using System;

namespace StockApp.Configurador;

/// <summary>
/// Ejecutable separado y mínimo: NO comparte el arranque de StockApp.Presentation (sin DI
/// container, sin Velopack, sin ApiClient). Solo necesita escribir un archivo de config y
/// pegarle a GET / de la API para probar la conexión.
/// </summary>
sealed class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
