namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción para persistir el tamaño, posición y estado de la ventana principal.
/// Permite que el wiring de la ventana sea testeable sin acoplarse directamente al
/// sistema de archivos.
/// </summary>
public interface IServicioEstadoVentana
{
    /// <summary>
    /// Carga el último estado guardado. Devuelve <c>null</c> si nunca se guardó nada,
    /// o si el archivo existe pero está corrupto/ilegible (defaults seguros).
    /// </summary>
    EstadoVentana? Cargar();

    /// <summary>
    /// Persiste el estado actual de la ventana. No lanza excepciones ante errores de IO
    /// (ej. disco lleno, permisos): la app debe poder seguir cerrando con normalidad.
    /// </summary>
    void Guardar(EstadoVentana estado);
}
