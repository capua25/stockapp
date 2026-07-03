using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Pantalla de bienvenida mostrada en la región central del shell tras el login.
/// Resuelve el bug de "región central vacía tras login": es el primer contenido
/// navegado dentro de ShellMainViewModel una vez que este queda establecido como
/// CurrentViewModel del shell.
/// </summary>
public partial class InicioViewModel : ViewModelBase
{
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;

    public string NombreUsuario =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Usuario";

    public string Saludo => $"¡Bienvenido, {NombreUsuario}!";

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public string RolTexto => EsAdmin ? "Administrador" : "Operador";

    public InicioViewModel(ICurrentSession session, INavigationService navigation)
    {
        _session    = session;
        _navigation = navigation;
    }

    // ── accesos rápidos: comunes (Admin + Operador) ───────────────────────────

    [RelayCommand]
    private void IrAProductos() => _navigation.Navegar<ProductoListViewModel>();

    [RelayCommand]
    private void IrARegistrarMovimiento() => _navigation.Navegar<MovimientoRegistroViewModel>();

    [RelayCommand]
    private void IrAHistorialMovimientos() => _navigation.Navegar<MovimientoHistorialViewModel>();

    // ── accesos rápidos: solo Admin ────────────────────────────────────────────

    [RelayCommand]
    private void IrAValorizacion() => _navigation.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void IrAAuditoria() => _navigation.Navegar<AuditoriaLogViewModel>();
}
