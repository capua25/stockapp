using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioGuardadoArchivo"/>.
/// Usa el <c>IStorageProvider</c> de la ventana principal de Avalonia para mostrar
/// un selector de archivo y escribir el contenido en disco.
/// No se testea unitariamente (es UI); en entornos headless devuelve <c>false</c> de forma segura.
/// </summary>
public class ServicioGuardadoArchivo : IServicioGuardadoArchivo
{
    /// <inheritdoc />
    public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido)
    {
        // Si no hay aplicación Avalonia inicializada (ej: tests headless), no se guarda nada.
        if (AvaloniaApp.Current is null)
            return Task.FromResult(false);

        // Garantizamos ejecución en el hilo de UI (Dispatcher.UIThread).
        return Dispatcher.UIThread.InvokeAsync(() => GuardarInternoAsync(contenido, nombreSugerido));
    }

    private static async Task<bool> GuardarInternoAsync(string contenido, string nombreSugerido)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        // La MainWindow es un TopLevel y expone StorageProvider.
        var storageProvider = lifetime?.MainWindow?.StorageProvider;

        if (storageProvider is null)
            return false;

        var archivo = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = nombreSugerido,
            DefaultExtension = "csv",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Archivo CSV")
                {
                    Patterns = new[] { "*.csv" },
                    MimeTypes = new[] { "text/csv" },
                },
            },
        });

        // El usuario canceló el selector.
        if (archivo is null)
            return false;

        await using var stream = await archivo.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contenido);

        return true;
    }
}
