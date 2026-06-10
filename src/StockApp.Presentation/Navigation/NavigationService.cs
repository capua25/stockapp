using System;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Navigation;

/// <summary>
/// Implementación de <see cref="INavigationService"/> basada en un factory (Func&lt;Type, object&gt;)
/// cableado al IServiceProvider en App.axaml.cs.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly Func<Type, object> _resolver;

    public ViewModelBase? Actual { get; private set; }

    public event Action? Cambiado;

    public NavigationService(Func<Type, object> resolver)
    {
        _resolver = resolver;
    }

    public void Navegar<TVm>() where TVm : ViewModelBase
    {
        Actual = (ViewModelBase)_resolver(typeof(TVm));
        Cambiado?.Invoke();
    }
}
