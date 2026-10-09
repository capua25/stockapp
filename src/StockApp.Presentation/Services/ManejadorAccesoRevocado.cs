using System;
using System.Threading.Tasks;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;

namespace StockApp.Presentation.Services;

/// <summary>
/// Qué hace la app ante un 403 con sesión válida (ApiSession.AccesoRevocado, cableado en
/// App.axaml.cs): refresca el cache de permisos, hace que la UI lo refleje y avisa al usuario.
/// Extraído de la lambda de App.axaml.cs para poder probar el orden con la View real.
///
/// Bug B3 (2026-10-09): antes el refresco solo actualizaba el cache; el sidebar y los botones de la
/// pantalla abierta seguían mostrando lo viejo hasta la próxima navegación. Orden: refresco, luego
/// <paramref name="alActualizarPermisos"/> (la UI deja de mostrar lo revocado), luego el aviso (que
/// espera a que el usuario lo cierre: la UI no puede quedar vieja mientras el modal está abierto).
/// El llamador garantiza el hilo de UI (uiDispatcher.Post).
/// </summary>
public static class ManejadorAccesoRevocado
{
    public static async Task ManejarAsync(
        ICurrentSession session,
        IAuthService authService,
        IConfirmacionService confirmacion,
        Action alActualizarPermisos)
    {
        var permisosAntes = session.PermisosActuales;
        await RefrescoPermisos.DispararBestEffortAsync(
            () => authService.ObtenerPermisosPropiosAsync(), "AccesoRevocado");
        alActualizarPermisos();
        var mensaje = AvisoAccesoRevocado.Resolver(permisosAntes, session.PermisosActuales);

        await confirmacion.InformarAsync(mensaje);
    }
}
