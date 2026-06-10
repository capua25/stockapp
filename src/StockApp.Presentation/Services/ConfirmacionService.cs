using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación stub de IConfirmacionService.
/// Devuelve false por defecto hasta que se conecte la View real con un diálogo modal Avalonia.
/// </summary>
public class ConfirmacionService : IConfirmacionService
{
    /// <inheritdoc />
    public Task<bool> PreguntarAsync(string mensaje)
    {
        // Stub: devuelve false (cancelar) por defecto.
        // En producción, este método abrirá un diálogo modal de Avalonia.
        return Task.FromResult(false);
    }
}
