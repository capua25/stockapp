using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción para "Ver" un adjunto: escribe los bytes a un archivo temporal y lo abre
/// con la aplicación por defecto del sistema operativo.
/// </summary>
public interface IServicioAperturaArchivo
{
    Task AbrirAsync(string nombreArchivo, byte[] contenido);
}
