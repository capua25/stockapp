using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción para elegir un archivo desde disco (Agregar adjunto). Molde:
/// IServicioGuardadoArchivo (Inc 6), pero de apertura en vez de guardado.
/// </summary>
public interface IServicioSeleccionArchivo
{
    /// <summary>
    /// Muestra el selector de archivo filtrando por PDF/JPG/PNG. Devuelve el nombre y los
    /// bytes leídos, o null si el usuario canceló.
    /// </summary>
    Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync();
}
