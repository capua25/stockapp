using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Servicio de confirmación: muestra un diálogo y devuelve la respuesta del usuario.
/// Se inyecta como Singleton y se mockea en tests de ViewModel.
/// </summary>
public interface IConfirmacionService
{
    /// <summary>
    /// Muestra un mensaje de confirmación al usuario y espera su respuesta.
    /// </summary>
    /// <param name="mensaje">Texto del mensaje a mostrar.</param>
    /// <returns>true si el usuario confirmó, false si canceló.</returns>
    Task<bool> PreguntarAsync(string mensaje);

    /// <summary>
    /// Muestra un mensaje informativo de una sola acción (sin opción de cancelar/confirmar)
    /// y espera a que el usuario lo cierre. Es el mecanismo único para informar errores
    /// amigables tanto desde los comandos de los ViewModels (ej. baja lógica de una entidad
    /// de catálogo ya inactiva) como desde la red de seguridad global de excepciones no
    /// manejadas del hilo de UI (ver App.axaml.cs).
    /// </summary>
    /// <param name="mensaje">Texto del mensaje a mostrar.</param>
    Task InformarAsync(string mensaje);
}
