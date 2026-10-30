using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// Hub de Reportes (spec §4.2): seis tarjetas que abren cada reporte. Los gates son getters sobre la sesión (no campos):
/// <see cref="ViewModelBase.NotificarPermisosActualizados"/> notifica todo y los bindings se re-evalúan en una
/// revocación en caliente sin override.
/// </summary>
public partial class ReportesViewModel : ViewModelBase
{
    private readonly ICurrentSession _session;
    private readonly INavigationService _navegacion;

    public ReportesViewModel(ICurrentSession session, INavigationService navegacion)
    {
        _session = session;
        _navegacion = navegacion;
    }

    /// <summary>Toda tarjeta exige <c>reportes.ver</c>: con él alguna tarjeta es visible.</summary>
    public bool PuedeVerReportes =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerReportes);

    /// <summary>
    /// Historial por producto: <c>ReporteStockService.ObtenerHistorialPorProductoAsync</c> verifica VerReportes pero delega en
    /// <c>MovimientoStockService.ObtenerHistorialAsync</c> (exige RegistrarMovimientos), y el buscador de producto pega a
    /// <c>GET /productos</c> (exige GestionarProductos). El gate exige el MÁXIMO de los permisos de las capas de abajo,
    /// nunca el mínimo (mismo criterio que PuedeRegistrarEntradaSalida del shell).
    /// </summary>
    public bool PuedeVerHistorialPorProducto =>
        _session.RolActual == RolUsuario.Admin ||
        (_session.PermisosActuales.Contains(Permisos.VerReportes) &&
         _session.PermisosActuales.Contains(Permisos.RegistrarMovimientos) &&
         _session.PermisosActuales.Contains(Permisos.GestionarProductos));

    [RelayCommand]
    private void AbrirValorizacion() => _navegacion.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void AbrirStockCategoria() => _navegacion.Navegar<StockCategoriaViewModel>();

    [RelayCommand]
    private void AbrirHistorialPorProducto() => _navegacion.Navegar<HistorialPorProductoViewModel>();

    [RelayCommand]
    private void AbrirMasMovidos() => _navegacion.Navegar<MasMovidosViewModel>();

    [RelayCommand]
    private void AbrirAuditoria() => _navegacion.Navegar<AuditoriaLogViewModel>();

    [RelayCommand]
    private void AbrirEstadisticaProyectos() => _navegacion.Navegar<ReporteProyectosViewModel>();
}
