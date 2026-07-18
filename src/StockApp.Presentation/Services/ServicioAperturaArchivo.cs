using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioAperturaArchivo"/>: guarda a un archivo
/// temporal (carpeta temp del SO, subcarpeta "stockapp-adjuntos") y lo abre con
/// ProcessStartInfo(UseShellExecute = true) — delega en la app asociada del SO (visor de
/// PDF, imágenes). No se testea unitariamente (lanza un proceso externo real).
/// </summary>
public class ServicioAperturaArchivo : IServicioAperturaArchivo
{
    public async Task AbrirAsync(string nombreArchivo, byte[] contenido)
    {
        var carpetaTemp = Path.Combine(Path.GetTempPath(), "stockapp-adjuntos");
        Directory.CreateDirectory(carpetaTemp);

        var ruta = Path.Combine(carpetaTemp, nombreArchivo);
        await File.WriteAllBytesAsync(ruta, contenido);

        Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
    }
}
