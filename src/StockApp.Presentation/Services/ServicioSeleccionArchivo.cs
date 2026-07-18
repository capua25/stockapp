using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioSeleccionArchivo"/>. Usa el IStorageProvider
/// de la ventana principal para elegir un archivo y lo lee a memoria. No se testea
/// unitariamente (es UI); en entornos headless devuelve null de forma segura.
/// </summary>
public class ServicioSeleccionArchivo : IServicioSeleccionArchivo
{
    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<(string, byte[])?>(null);

        return Dispatcher.UIThread.InvokeAsync(SeleccionarInternoAsync);
    }

    private static async Task<(string, byte[])?> SeleccionarInternoAsync()
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var storageProvider = lifetime?.MainWindow?.StorageProvider;
        if (storageProvider is null)
            return null;

        var archivos = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Documentos e imágenes")
                {
                    Patterns = new[] { "*.pdf", "*.jpg", "*.jpeg", "*.png" },
                    MimeTypes = new[] { "application/pdf", "image/jpeg", "image/png" },
                },
            },
        });

        if (archivos.Count == 0)
            return null;

        var archivo = archivos[0];
        await using var stream = await archivo.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        return (archivo.Name, ms.ToArray());
    }
}
