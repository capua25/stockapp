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
/// ViewModel del shell principal post-login. Hostea el menú lateral y la región de contenido.
/// Los ítems de "Tablas maestras" solo son visibles para Admin; "Productos" está disponible
/// para Admin y Operador.
/// </summary>
public partial class ShellMainViewModel : ViewModelBase
{
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    /// <summary>
    /// ViewModel activo en la región de contenido. Se actualiza cuando el INavigationService
    /// notifica un cambio via el evento Cambiado.
    /// </summary>
    [ObservableProperty]
    private ViewModelBase? _currentContent;

    public ShellMainViewModel(ICurrentSession session, INavigationService navigation)
    {
        _session    = session;
        _navigation = navigation;

        // Suscribirse al evento del servicio para actualizar la región de contenido
        _navigation.Cambiado += () =>
        {
            // El contenido del shell nunca puede ser el propio shell: evita la recursión
            // ShellMainView dentro de ShellMainView (StackOverflow al renderizar).
            // Se compara por referencia contra 'this' (no con el tipo) porque es la guardia
            // más estricta posible: excluye exactamente la instancia que causaría el ciclo,
            // sin descartar por error una futura subclase de ShellMainViewModel que sí
            // debiera poder navegarse como contenido válido.
            if (!ReferenceEquals(_navigation.Actual, this))
                CurrentContent = _navigation.Actual;
        };
    }

    // ── comandos de navegación ────────────────────────────────────────────────

    [RelayCommand]
    private void NavProductos() => _navigation.Navegar<ProductoListViewModel>();

    [RelayCommand]
    private void NavCategorias() => _navigation.Navegar<CategoriaListViewModel>();

    [RelayCommand]
    private void NavProveedores() => _navigation.Navegar<ProveedorListViewModel>();

    [RelayCommand]
    private void NavUnidadesMedida() => _navigation.Navegar<UnidadMedidaListViewModel>();

    // ── Movimientos (Inc 5): Operador + Admin ─────────────────────────────────

    [RelayCommand]
    private void NavMovimientos() => _navigation.Navegar<MovimientoRegistroViewModel>();

    [RelayCommand]
    private void NavHistorialMovimientos() => _navigation.Navegar<MovimientoHistorialViewModel>();

    // ── Reportes (Inc 6): solo Admin ──────────────────────────────────────────

    [RelayCommand]
    private void NavValorizacion() => _navigation.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void NavStockCategoria() => _navigation.Navegar<StockCategoriaViewModel>();

    [RelayCommand]
    private void NavHistorialPorProducto() => _navigation.Navegar<HistorialPorProductoViewModel>();

    [RelayCommand]
    private void NavMasMovidos() => _navigation.Navegar<MasMovidosViewModel>();

    [RelayCommand]
    private void NavAuditoriaLog() => _navigation.Navegar<AuditoriaLogViewModel>();
}
