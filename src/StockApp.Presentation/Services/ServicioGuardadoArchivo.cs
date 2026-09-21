using System.Collections.Generic;
using System.IO;
using System.Threading;
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

        var archivo = await storageProvider.SaveFilePickerAsync(ConstruirOpcionesGuardadoTexto(nombreSugerido));

        // El usuario canceló el selector.
        if (archivo is null)
            return false;

        await using var stream = await archivo.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(contenido);

        return true;
    }

    /// <summary>
    /// Construye las opciones del selector nativo para el camino de TEXTO (hoy, únicamente
    /// CSV). Extraído a un método propio, testeable sin bootstrap de Avalonia (spec 2026-09-18,
    /// export PDF), para poder guardar por mutación que este camino no cambia de comportamiento
    /// al agregar <c>extension</c>/<c>tipoMime</c> al camino de BYTES (Task 8).
    /// </summary>
    internal static FilePickerSaveOptions ConstruirOpcionesGuardadoTexto(string nombreSugerido) =>
        new()
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
        };

    /// <inheritdoc />
    public Task<bool> GuardarBytesAsync(
        Stream contenido,
        string nombreSugerido,
        CancellationToken ct = default,
        string? extension = null,
        string? tipoMime = null)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult(false);

        return Dispatcher.UIThread.InvokeAsync(
            () => GuardarBytesInternoAsync(contenido, nombreSugerido, ct, extension, tipoMime));
    }

    private static async Task<bool> GuardarBytesInternoAsync(
        Stream contenido, string nombreSugerido, CancellationToken ct, string? extension, string? tipoMime)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;
        var storageProvider = lifetime?.MainWindow?.StorageProvider;

        if (storageProvider is null)
            return false;

        var opciones = new FilePickerSaveOptions { SuggestedFileName = nombreSugerido };

        // Sin extension no se arma ningún filtro (mismo comportamiento que antes de esta firma:
        // backups/logs guardan cualquier extensión, el selector no debe restringirla).
        if (extension is not null)
        {
            opciones.DefaultExtension = extension;
            opciones.FileTypeChoices = new List<FilePickerFileType>
            {
                new($"Archivo {extension.ToUpperInvariant()}")
                {
                    Patterns = new[] { $"*.{extension}" },
                    MimeTypes = tipoMime is null ? null : new[] { tipoMime },
                },
            };
        }

        var archivo = await storageProvider.SaveFilePickerAsync(opciones);

        if (archivo is null)
            return false;

        await using var destino = await archivo.OpenWriteAsync();
        await contenido.CopyToAsync(destino, ct);

        return true;
    }
}
