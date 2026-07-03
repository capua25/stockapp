namespace StockApp.Presentation.Services;

/// <summary>
/// Expone metadata de la app en ejecución — hoy solo el número de versión, leído del
/// assembly. Se usa para mostrar la versión en la UI (login y shell) y para validar
/// visualmente el flujo de actualización (ver el número cambiar tras actualizar).
/// </summary>
public interface IInfoApp
{
    /// <summary>
    /// Número de versión de la app, sin el prefijo 'v' (ej. "0.1.1").
    /// </summary>
    string Version { get; }
}
