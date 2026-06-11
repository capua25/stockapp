using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de IConfirmacionService.
/// Abre un diálogo modal de Avalonia sobre la ventana principal y devuelve la respuesta del usuario.
/// </summary>
public class ConfirmacionService : IConfirmacionService
{
    /// <inheritdoc />
    public Task<bool> PreguntarAsync(string mensaje)
    {
        // Si no hay aplicación Avalonia inicializada (ej: tests headless), rechazar de forma segura.
        if (AvaloniaApp.Current is null)
            return Task.FromResult(false);

        // Garantizamos ejecución en el hilo de UI (Dispatcher.UIThread).
        return Dispatcher.UIThread.InvokeAsync(() => MostrarDialogoAsync(mensaje));
    }

    private static async Task<bool> MostrarDialogoAsync(string mensaje)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var owner = lifetime?.MainWindow;

        if (owner is null)
        {
            // No hay ventana principal disponible: rechazar de forma segura.
            return false;
        }

        var dialog = new ConfirmacionDialog(mensaje);
        var resultado = await dialog.ShowDialog<bool>(owner);
        return resultado;
    }
}
