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
}
