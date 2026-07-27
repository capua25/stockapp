using System.IO;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción para guardar contenido de texto en disco mostrando un selector de archivo.
/// Permite que los ViewModels que exportan datos (CSV, etc.) sean testeables sin acoplar
/// la lógica al <c>IStorageProvider</c> de Avalonia.
/// </summary>
public interface IServicioGuardadoArchivo
{
    /// <summary>
    /// Muestra el selector de archivo y escribe el <paramref name="contenido"/> en la ubicación elegida.
    /// </summary>
    /// <param name="contenido">Texto a escribir en el archivo.</param>
    /// <param name="nombreSugerido">Nombre de archivo sugerido en el selector (ej: "valorizacion.csv").</param>
    /// <returns><c>true</c> si el usuario eligió una ubicación y el archivo se guardó; <c>false</c> si canceló.</returns>
    Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido);

    /// <summary>
    /// Muestra el selector de archivo y copia <paramref name="contenido"/> directo al Stream de
    /// escritura del archivo elegido — sin bufferear el contenido completo en memoria (Inc
    /// Backups: un dump de varios MB/GB no puede pasar por un solo byte[]).
    /// </summary>
    /// <param name="contenido">Stream de origen (ej. el body de la respuesta HTTP de descarga).</param>
    /// <param name="nombreSugerido">Nombre de archivo sugerido en el selector.</param>
    /// <returns><c>true</c> si el usuario eligió una ubicación y el archivo se guardó; <c>false</c> si canceló.</returns>
    Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido);
}
