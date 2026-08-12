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

    /// <summary>
    /// Pide al usuario un texto libre obligatorio (módulo Documentos, spec 2026-08-11:
    /// anular y reabrir un documento administrativo exigen motivo). No valida "no vacío" —
    /// esa validación vive en el servicio de Application (documentos.gestionar/administrar
    /// pasa el texto crudo); este método solo recolecta lo que el usuario tipeó.
    /// </summary>
    /// <param name="titulo">Título de la ventana del diálogo.</param>
    /// <param name="mensaje">Texto explicativo mostrado sobre el campo de texto.</param>
    /// <returns>El texto tipeado, o null si el usuario canceló.</returns>
    Task<string?> PedirTextoAsync(string titulo, string mensaje);
}
