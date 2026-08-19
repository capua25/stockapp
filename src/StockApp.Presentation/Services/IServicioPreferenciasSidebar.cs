namespace StockApp.Presentation.Services;

/// <summary>
/// Persistencia local de las preferencias del menu lateral. Mismo contrato que
/// IServicioEstadoVentana: Cargar devuelve null si no hay nada guardado o si el archivo esta
/// roto, y Guardar nunca propaga un fallo de IO.
/// </summary>
public interface IServicioPreferenciasSidebar
{
    /// <summary>Devuelve null si no hay preferencias guardadas o si el archivo no se pudo leer.</summary>
    PreferenciasSidebar? Cargar();

    /// <summary>Guarda las preferencias. Si falla el IO, no hace nada y no propaga.</summary>
    void Guardar(PreferenciasSidebar preferencias);
}
