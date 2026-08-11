using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Ejecuta una operación asíncrona en modo "mejor esfuerzo" (spec 2026-08-10): nunca propaga
/// la excepción, la deja registrada en crash.log vía Program.LogFatal con el origen indicado
/// — "mejor esfuerzo" no significa "invisible". Consumido por los cuatro puntos del sistema de
/// permisos que refrescan el cache local sin poder bloquear ni interrumpir el flujo que los
/// dispara: login (LoginViewModel, esta task), navegación entre secciones (ShellMainViewModel,
/// Task 14), cambio de usuario seleccionado en el panel de permisos (PanelPermisosViewModel,
/// Task 13) y el aviso de 403 (App.axaml.cs, Task 15).
///
/// Devuelve el Task que envuelve la operación (nunca lanza): quien lo llame puede ignorarlo
/// (fire-and-forget puro, `_ = RefrescoPermisos.DispararBestEffortAsync(...)`) o guardarlo en
/// un campo `internal Task` para que un test lo awaite de forma determinista — mismo patrón que
/// ShellViewModel._tareaActualizacion / ProductoListViewModel._tareaDebounce ya usan en este
/// repo para el mismo problema (sincronizar un test con trabajo fire-and-forget sin Task.Delay).
/// </summary>
public static class RefrescoPermisos
{
    public static async Task DispararBestEffortAsync(Func<Task> operacion, string origen)
    {
        try
        {
            await operacion();
        }
        catch (Exception ex)
        {
            StockApp.Presentation.Program.LogFatal(origen, ex);
        }
    }
}
