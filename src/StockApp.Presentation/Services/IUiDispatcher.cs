using System;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción del dispatcher de UI. Permite marshalear acciones al hilo de UI desde
/// código que corre en el thread-pool, sin acoplar el código a Avalonia.Threading.Dispatcher
/// directamente — lo que rompería en tests unitarios (no hay Application de Avalonia inicializada).
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Encola una acción para ejecutarse en el hilo de UI.
    /// </summary>
    /// <param name="accion">Acción a ejecutar en el hilo de UI.</param>
    void Post(Action accion);
}
