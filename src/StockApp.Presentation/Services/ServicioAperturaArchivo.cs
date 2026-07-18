using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioAperturaArchivo"/>: guarda a un archivo
/// temporal (carpeta temp del SO, subcarpeta "stockapp-adjuntos") y lo abre con
/// ProcessStartInfo(UseShellExecute = true) — delega en la app asociada del SO (visor de
/// PDF, imágenes). El nombre y la extensión del adjunto los controla quien lo sube, así
/// que se sanitizan/validan antes de tocar el filesystem o lanzar un proceso (ver
/// <see cref="SanitizarYValidarExtension"/>, testeado unitariamente). El resto (I/O real,
/// Process.Start) no se testea (lanza un proceso externo real).
/// </summary>
public class ServicioAperturaArchivo : IServicioAperturaArchivo
{
    public async Task AbrirAsync(string nombreArchivo, byte[] contenido)
    {
        var carpetaTemp = Path.Combine(Path.GetTempPath(), "stockapp-adjuntos");
        Directory.CreateDirectory(carpetaTemp);

        var extension = SanitizarYValidarExtension(nombreArchivo);

        // Nombre local generado (no el del atacante): evita que la extensión/nombre
        // controlado por quien subió el adjunto determine qué se ejecuta o dónde se escribe.
        var rutaSegura = Path.Combine(carpetaTemp, Guid.NewGuid().ToString("N") + extension);

        await File.WriteAllBytesAsync(rutaSegura, contenido);

        Process.Start(new ProcessStartInfo(rutaSegura) { UseShellExecute = true });
    }

    /// <summary>
    /// Lógica pura: quita cualquier componente de path del nombre recibido (defensa contra
    /// path traversal — "../../x.pdf", rutas absolutas) y valida la extensión resultante
    /// contra la whitelist única de <see cref="AdjuntoValidador"/>. Devuelve la extensión
    /// (en minúsculas, con punto) o lanza <see cref="InvalidOperationException"/> si no es
    /// una extensión permitida.
    /// </summary>
    internal static string SanitizarYValidarExtension(string nombreArchivo)
    {
        var nombreSeguro = Path.GetFileName(nombreArchivo);
        var extension = Path.GetExtension(nombreSeguro).ToLowerInvariant();

        if (!AdjuntoValidador.ExtensionesPermitidas.Contains(extension))
            throw new InvalidOperationException($"Extensión de archivo no permitida: '{extension}'.");

        return extension;
    }
}
