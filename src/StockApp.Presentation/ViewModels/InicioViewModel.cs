using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
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
    private readonly ICurrentSession        _session;
    private readonly INavigationService     _navigation;
    private readonly IFinanzasVistasService _finanzasVistas;

    public string NombreUsuario =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Usuario";

    public string Saludo => $"¡Bienvenido, {NombreUsuario}!";

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public string RolTexto => EsAdmin ? "Administrador" : "Operador";

    [ObservableProperty] private bool _mostrarAvisoVencimientos;
    [ObservableProperty] private int _cantidadVencidas;
    [ObservableProperty] private int _cantidadAVencer7Dias;

    public InicioViewModel(
        ICurrentSession session, INavigationService navigation, IFinanzasVistasService finanzasVistas)
    {
        _session        = session;
        _navigation     = navigation;
        _finanzasVistas = finanzasVistas;
    }

    /// <summary>
    /// Carga el aviso de vencimientos (spec §7.5: "al abrir la app, aviso en Inicio si hay
    /// facturas vencidas o por vencer en la semana"). Sin VerFinanzas o si la API falla, el
    /// aviso simplemente no se muestra — Inicio nunca debe romper (catch silencioso).
    /// </summary>
    public async Task CargarAsync()
    {
        try
        {
            var calendario = await _finanzasVistas.ObtenerCalendarioPagosAsync();
            CantidadVencidas = calendario.Vencidas.Count;
            CantidadAVencer7Dias = calendario.AVencer7Dias.Count;
            MostrarAvisoVencimientos = CantidadVencidas > 0 || CantidadAVencer7Dias > 0;
        }
        catch (Exception)
        {
            MostrarAvisoVencimientos = false;
        }
    }

    // ── accesos rápidos: comunes (Admin + Operador) ───────────────────────────

    [RelayCommand]
    private void IrAProductos() => _navigation.Navegar<ProductoListViewModel>();

    [RelayCommand]
    private void IrARegistrarEntrada() => _navigation.Navegar<EntradaRegistroViewModel>();

    [RelayCommand]
    private void IrARegistrarSalida() => _navigation.Navegar<SalidaRegistroViewModel>();

    [RelayCommand]
    private void IrAHistorialMovimientos() => _navigation.Navegar<MovimientoHistorialViewModel>();

    [RelayCommand]
    private void IrACalendarioPagos() => _navigation.Navegar<CalendarioPagosViewModel>();

    // ── accesos rápidos: solo Admin ────────────────────────────────────────────

    [RelayCommand]
    private void IrAValorizacion() => _navigation.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void IrAAuditoria() => _navigation.Navegar<AuditoriaLogViewModel>();
}
