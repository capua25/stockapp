using Avalonia;
using Optris.Icons.Avalonia;
using Optris.Icons.Avalonia.MaterialDesign;
using System;
using System.Threading.Tasks;
using Velopack;
using StockApp.Presentation.Services;

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
        // %LocalAppData%\GestionMunicipal\logs\crash.log para diagnosticar cierres silenciosos
        // (exit 0 sin excepción visible en Event Viewer).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("AppDomain", (Exception)e.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ManejarExcepcionNoObservada("UnobservedTask", e.Exception, new RegistroFallosArchivo());
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
    {
        // Registro del proveedor de iconos Material Design (Tarea 3, UI Kit).
        // Se hace acá (no en App.axaml.cs) para que también esté disponible en el
        // previewer de diseño XAML, que solo invoca este método y no App.OnFrameworkInitializationCompleted.
        IconProvider.Current.Register<MaterialDesignIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .WithDataAnnotationsValidation()
            .LogToTrace();
    }

    /// <summary>
    /// Escribe una entrada de crash a %LocalAppData%\GestionMunicipal\logs\crash.log.
    /// Delega en <see cref="RegistroFallosArchivo"/> (fix 2026-08-20: misma lógica de
    /// escritura + rotación reusada por RefrescoPermisos vía IRegistroFallos, sin duplicarla
    /// acá). Sigue siendo el punto de entrada legítimo para AppDomain.UnhandledException y el
    /// catch de Main — ninguno de esos corre durante `dotnet test`, así que no necesitan pasar
    /// por la abstracción inyectable. UnobservedTask ya NO llama acá directo (fix 2026-08-20):
    /// pasa por <see cref="ManejarExcepcionNoObservada"/>, que decide primero si debe quedarse
    /// en silencio (ver <see cref="PoliticaExcepcionSilenciosa"/>); lo mismo para
    /// Dispatcher.UIThread.UnhandledException en App.axaml.cs (ver
    /// <see cref="App.ManejarExcepcionUiThread"/>).
    /// </summary>
    internal static void LogFatal(string origen, Exception ex) =>
        new RegistroFallosArchivo().LogFatal(origen, ex);

    /// <summary>
    /// Cuerpo del handler de <c>TaskScheduler.UnobservedTaskException</c>, extraído para poder
    /// testearlo (fix 2026-08-20) sin escribir en el crash.log real -- por eso recibe
    /// <paramref name="registro"/> explícito en vez de llamar a <see cref="LogFatal"/>
    /// directamente (ese overload instancia un <see cref="RegistroFallosArchivo"/> real). Ver
    /// <see cref="PoliticaExcepcionSilenciosa"/>: un 403/401 legítimo (UnauthorizedAccessException,
    /// típicamente envuelta en <see cref="AggregateException"/> cuando la Task de un
    /// [RelayCommand] asíncrono falla sin que nadie observe la excepción) ya fue informado por
    /// AuthTokenHandler vía ApiSession.AccesoRevocado -- no se loguea de nuevo acá. A diferencia
    /// del handler de Dispatcher.UIThread (App.axaml.cs), este NUNCA muestra un mensaje al
    /// usuario, silencioso o no.
    /// </summary>
    internal static void ManejarExcepcionNoObservada(string origen, Exception ex, IRegistroFallos registro)
    {
        if (PoliticaExcepcionSilenciosa.EsAccesoRevocado(ex))
            return;

        registro.LogFatal(origen, ex);
    }
}
