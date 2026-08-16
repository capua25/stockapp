using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Calendario de pagos" (spec §7.5): facturas vencidas, a vencer en 7/30 días y
/// pagos recientes. "Registrar pago" trae el Gasto completo y navega a PagosGastoViewModel.
/// </summary>
public partial class CalendarioPagosViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly IGastoService          _gastoService;
    private readonly ICurrentSession        _session;
    private readonly INavigationService     _navigation;

    public ObservableCollection<FacturaCalendarioDto> Vencidas { get; } = new();
    public ObservableCollection<FacturaCalendarioDto> AVencer7Dias { get; } = new();
    public ObservableCollection<FacturaCalendarioDto> AVencer30Dias { get; } = new();
    public ObservableCollection<PagoRecienteDto> PagosRecientes { get; } = new();

    /// <summary>
    /// Gatea los botones "Registrar pago" (bugfix 2026-08-15). Esto NO es un gap de seguridad:
    /// PagosGastoView (destino de la navegación) ya gatea la acción por su cuenta, exigiendo
    /// Permisos.RegistrarPagos — un Operador sin ese permiso ya no puede ejecutar el pago ahí.
    /// Esto es coherencia de navegación: evita que un link de esta pantalla (que solo exige
    /// Permisos.VerFinanzas) ofrezca algo inalcanzable. Se OCULTA (no se deshabilita), mismo
    /// patrón que GastosViewModel.PuedeRegistrarPagos: IsVisible="{Binding Puede*}", nunca
    /// IsEnabled. No se refresca en caliente: CalendarioPagosViewModel se registra AddTransient
    /// y se recrea en cada navegación a la pantalla.
    /// </summary>
    public bool PuedeRegistrarPagos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarPagos);

    public CalendarioPagosViewModel(
        IFinanzasVistasService service, IGastoService gastoService,
        ICurrentSession session, INavigationService navigation)
    {
        _service      = service;
        _gastoService = gastoService;
        _session      = session;
        _navigation   = navigation;
    }

    public async Task CargarAsync()
    {
        try
        {
            var calendario = await _service.ObtenerCalendarioPagosAsync();

            Vencidas.Clear();
            foreach (var f in calendario.Vencidas) Vencidas.Add(f);
            AVencer7Dias.Clear();
            foreach (var f in calendario.AVencer7Dias) AVencer7Dias.Add(f);
            AVencer30Dias.Clear();
            foreach (var f in calendario.AVencer30Dias) AVencer30Dias.Add(f);
            PagosRecientes.Clear();
            foreach (var p in calendario.PagosRecientes) PagosRecientes.Add(p);
        }
        catch (UnauthorizedAccessException)
        {
            // Red de contención (bugfix 2026-08-15): CargarAsync la dispara la View
            // (DataContextChanged) fire-and-forget — una excepción no atrapada acá escala a
            // Dispatcher.UIThread.UnhandledException (App.axaml.cs), que loguea a crash.log y
            // muestra el genérico "Ocurrió un error inesperado" como si fuera un bug real. Un
            // 403 ya se avisó ANTES de llegar acá: AuthTokenHandler dispara
            // ApiSession.AccesoRevocado apenas ve el 403 en la respuesta HTTP, y App.axaml.cs
            // ya informa "Tus permisos cambiaron...". Mismo criterio que
            // GastosViewModel.CargarAsync (ec0696c): silencioso, para no duplicar el aviso.
        }
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    [RelayCommand]
    private async Task RegistrarPagoAsync(FacturaCalendarioDto? fila)
    {
        if (fila is null) return;
        var gasto = await _gastoService.ObtenerPorIdAsync(fila.GastoId);
        _navigation.Navegar<PagosGastoViewModel>(vm =>
        {
            vm.CargarParaGasto(gasto);
            vm.ConfigurarVolver(() => _navigation.Navegar<CalendarioPagosViewModel>());
        });
    }
}
