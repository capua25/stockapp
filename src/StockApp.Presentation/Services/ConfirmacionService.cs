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

    /// <inheritdoc />
    public Task InformarAsync(string mensaje)
    {
        // Mismo criterio defensivo que PreguntarAsync: sin aplicación Avalonia inicializada
        // (ej: tests headless), no hay dónde mostrar el diálogo — no hacemos nada.
        if (AvaloniaApp.Current is null)
            return Task.CompletedTask;

        return Dispatcher.UIThread.InvokeAsync(() => MostrarMensajeAsync(mensaje));
    }

    private static async Task MostrarMensajeAsync(string mensaje)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var owner = lifetime?.MainWindow;

        if (owner is null)
        {
            // No hay ventana principal disponible: no hay nada más que hacer.
            return;
        }

        var dialog = new MensajeDialog(mensaje);
        await dialog.ShowDialog(owner);
    }
}
