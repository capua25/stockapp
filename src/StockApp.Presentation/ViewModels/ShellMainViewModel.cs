using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
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
    private readonly IInfoApp           _infoApp;
    private readonly IConfirmacionService _confirmacion;

    /// <summary>
    /// Se dispara cuando el usuario confirma "Cerrar sesión" y la sesión ya fue limpiada
    /// (ICurrentSession.CerrarSesion()). La composition root (ShellViewModel) lo cablea a
    /// la navegación de vuelta al login, igual que BloqueoLicenciaViewModel.LicenciaActivada
    /// o ResetAdminViewModel.Volver.
    /// </summary>
    public event Action? CerrarSesionSolicitado;

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    /// <summary>
    /// Número de versión de la app para mostrar al pie del menú lateral (ej. "v0.1.1").
    /// </summary>
    public string VersionTexto => $"v{_infoApp.Version}";

    /// <summary>
    /// ViewModel activo en la región de contenido. Se actualiza cuando el INavigationService
    /// notifica un cambio via el evento Cambiado.
    /// </summary>
    [ObservableProperty]
    private ViewModelBase? _currentContent;

    public ShellMainViewModel(
        ICurrentSession session,
        INavigationService navigation,
        IInfoApp infoApp,
        IConfirmacionService confirmacion)
    {
        _session      = session;
        _navigation   = navigation;
        _infoApp      = infoApp;
        _confirmacion = confirmacion;

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

    /// <summary>
    /// Nombre lógico de la sección actualmente activa en el sidebar (ej. "Productos").
    /// Se usa desde ShellMainView.axaml para resaltar el ítem de navegación seleccionado
    /// (Classes.active + ObjectConverters.Equal). Null hasta la primera navegación.
    /// </summary>
    [ObservableProperty]
    private string? _seccionActiva;

    // ── comandos de navegación ────────────────────────────────────────────────

    [RelayCommand]
    private void NavInicio()
    {
        SeccionActiva = "Inicio";
        _navigation.Navegar<InicioViewModel>();
    }

    [RelayCommand]
    private void NavProductos()
    {
        SeccionActiva = "Productos";
        _navigation.Navegar<ProductoListViewModel>();
    }

    [RelayCommand]
    private void NavCategorias()
    {
        SeccionActiva = "Categorias";
        _navigation.Navegar<CategoriaListViewModel>();
    }

    [RelayCommand]
    private void NavProveedores()
    {
        SeccionActiva = "Proveedores";
        _navigation.Navegar<ProveedorListViewModel>();
    }

    [RelayCommand]
    private void NavUnidadesMedida()
    {
        SeccionActiva = "UnidadesMedida";
        _navigation.Navegar<UnidadMedidaListViewModel>();
    }

    // ── Movimientos (Inc 5): Operador + Admin ─────────────────────────────────

    [RelayCommand]
    private void NavRegistrarEntrada()
    {
        SeccionActiva = "RegistrarEntrada";
        _navigation.Navegar<EntradaRegistroViewModel>();
    }

    [RelayCommand]
    private void NavRegistrarSalida()
    {
        SeccionActiva = "RegistrarSalida";
        _navigation.Navegar<SalidaRegistroViewModel>();
    }

    [RelayCommand]
    private void NavHistorialMovimientos()
    {
        SeccionActiva = "HistorialMovimientos";
        _navigation.Navegar<MovimientoHistorialViewModel>();
    }

    // ── Reportes (Inc 6): solo Admin ──────────────────────────────────────────

    [RelayCommand]
    private void NavValorizacion()
    {
        SeccionActiva = "Valorizacion";
        _navigation.Navegar<ValorizacionViewModel>();
    }

    [RelayCommand]
    private void NavStockCategoria()
    {
        SeccionActiva = "StockCategoria";
        _navigation.Navegar<StockCategoriaViewModel>();
    }

    [RelayCommand]
    private void NavHistorialPorProducto()
    {
        SeccionActiva = "HistorialPorProducto";
        _navigation.Navegar<HistorialPorProductoViewModel>();
    }

    [RelayCommand]
    private void NavMasMovidos()
    {
        SeccionActiva = "MasMovidos";
        _navigation.Navegar<MasMovidosViewModel>();
    }

    [RelayCommand]
    private void NavAuditoriaLog()
    {
        SeccionActiva = "AuditoriaLog";
        _navigation.Navegar<AuditoriaLogViewModel>();
    }

    // ── Finanzas — Fase 1: Admin y Operador ───────────────────────────────────

    [RelayCommand]
    private void NavMaestrosFinanzas()
    {
        SeccionActiva = "MaestrosFinanzas";
        _navigation.Navegar<MaestrosFinanzasViewModel>();
    }

    // ── Cerrar sesión ──────────────────────────────────────────────────────────

    /// <summary>
    /// Pide confirmación y, si el usuario acepta, limpia la sesión actual (token JWT +
    /// snapshot de identidad, vía ICurrentSession.CerrarSesion()) y dispara
    /// <see cref="CerrarSesionSolicitado"/> para que la composition root navegue al login.
    /// </summary>
    [RelayCommand]
    private async Task CerrarSesionAsync()
    {
        var confirmar = await _confirmacion.PreguntarAsync("¿Cerrar la sesión?");
        if (!confirmar) return;

        _session.CerrarSesion();
        CerrarSesionSolicitado?.Invoke();
    }
}
