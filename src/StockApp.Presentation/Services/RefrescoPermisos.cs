using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Ejecuta una operación asíncrona en modo "mejor esfuerzo" (spec 2026-08-10): nunca propaga
/// la excepción, la deja registrada en crash.log vía <see cref="IRegistroFallos"/> con el
/// origen indicado — "mejor esfuerzo" no significa "invisible". Consumido por los cuatro
/// puntos del sistema de permisos que refrescan el cache local sin poder bloquear ni
/// interrumpir el flujo que los dispara: login (LoginViewModel, esta task), navegación entre
/// secciones (ShellMainViewModel, Task 14), cambio de usuario seleccionado en el panel de
/// permisos (PanelPermisosViewModel, Task 13) y el aviso de 403 (App.axaml.cs, Task 15).
///
/// Devuelve el Task que envuelve la operación (nunca lanza): quien lo llame puede ignorarlo
/// (fire-and-forget puro, `_ = RefrescoPermisos.DispararBestEffortAsync(...)`) o guardarlo en
/// un campo `internal Task` para que un test lo awaite de forma determinista — mismo patrón que
/// ShellViewModel._tareaActualizacion / ProductoListViewModel._tareaDebounce ya usan en este
/// repo para el mismo problema (sincronizar un test con trabajo fire-and-forget sin Task.Delay).
/// </summary>
public static class RefrescoPermisos
{
    /// <summary>
    /// Registro de fallos usado por <see cref="DispararBestEffortAsync"/>. Antes de este fix
    /// (2026-08-20) esta clase llamaba directo a Program.LogFatal, así que cada corrida de
    /// `dotnet test` escribía en el crash.log real del usuario. Default seguro (nunca null):
    /// el mismo comportamiento de producción de siempre hasta que el composition root
    /// (App.axaml.cs, ConfigurarServicios + OnFrameworkInitializationCompleted) o el bootstrap
    /// de los proyectos de test (TestBootstrap, vía [ModuleInitializer]) lo reconfiguren.
    /// </summary>
    private static IRegistroFallos _registroFallos = new RegistroFallosArchivo();

    /// <summary>
    /// Reemplaza el registro de fallos. Llamado UNA vez desde el composition root de
    /// producción y desde el bootstrap de cada proyecto de test — no pensado para que
    /// tests individuales lo reconfiguren en el medio de una corrida (xUnit ejecuta
    /// colecciones en paralelo por default; pisar esto a mitad de camino sería una carrera).
    /// </summary>
    internal static void ConfigurarRegistroFallos(IRegistroFallos registroFallos) =>
        _registroFallos = registroFallos ?? throw new ArgumentNullException(nameof(registroFallos));

    public static async Task DispararBestEffortAsync(Func<Task> operacion, string origen)
    {
        try
        {
            await operacion();
        }
        catch (Exception ex)
        {
            _registroFallos.LogFatal(origen, ex);
        }
    }
}
