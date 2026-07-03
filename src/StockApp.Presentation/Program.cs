using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;
using Velopack;

namespace StockApp.Presentation;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Captura global de excepciones no manejadas: escribe a
        // %LocalAppData%\StockApp\logs\crash.log para diagnosticar cierres silenciosos
        // (exit 0 sin excepción visible en Event Viewer).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("AppDomain", (Exception)e.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal("UnobservedTask", e.Exception);
            e.SetObserved();
        };

        try
        {
            // OBLIGATORIO Velopack: primera línea, antes de cualquier API de Avalonia.
            // En dev (sin instalar vía Velopack) esta llamada simplemente retorna.
            VelopackApp.Build().Run();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogFatal("Main", ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Escribe una entrada de crash a %LocalAppData%\StockApp\logs\crash.log.
    /// Nunca debe tirar: si falla la escritura, se traga la excepción silenciosamente
    /// para no enmascarar el error original que se está intentando loguear.
    /// </summary>
    internal static void LogFatal(string origen, Exception ex)
    {
        try
        {
            var logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StockApp",
                "logs");

            Directory.CreateDirectory(logsDir);

            var logPath = Path.Combine(logsDir, "crash.log");

            var entrada =
                $"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fffzzz}] origen={origen} " +
                $"tipo={ex.GetType().FullName} mensaje={ex.Message}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}";

            File.AppendAllText(logPath, entrada);
        }
        catch
        {
            // El logger nunca debe tirar: si falla la escritura, no hay nada más que hacer acá.
        }
    }

}
