using System;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Navigation;

/// <summary>
/// Servicio de navegación entre ViewModels. El shell escucha el evento <see cref="Cambiado"/>
/// y ViewLocator resuelve la vista correspondiente por convención (XxxViewModel → XxxView).
/// </summary>
public interface INavigationService
{
    /// <summary>ViewModel actualmente visible, o null si no se navegó aún.</summary>
    ViewModelBase? Actual { get; }

    /// <summary>Se dispara cada vez que <see cref="Actual"/> cambia.</summary>
    event Action? Cambiado;

    /// <summary>
    /// Resuelve <typeparamref name="TVm"/> desde el contenedor DI y lo establece como VM actual.
    /// </summary>
    void Navegar<TVm>() where TVm : ViewModelBase;

    /// <summary>
    /// Resuelve <typeparamref name="TVm"/> desde el contenedor DI, ejecuta <paramref name="inicializar"/>
    /// sobre la instancia recién creada (por ejemplo, para precargar un modo edición con datos de
    /// un item seleccionado en otra pantalla) y luego la establece como VM actual. El VM sigue
    /// resolviéndose fresco desde DI, igual que <see cref="Navegar{TVm}()"/> — este overload solo
    /// agrega un paso de inicialización antes de publicarlo.
    /// </summary>
    void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase;
}
