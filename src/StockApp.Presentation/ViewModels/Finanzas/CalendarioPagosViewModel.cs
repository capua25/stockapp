using System;
using System.Collections.Generic;
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
    /// IsEnabled. Un 403 dispara ManejadorAccesoRevocado: la base notifica todos los bindings de
    /// la pantalla abierta (ViewModelBase.NotificarPermisosActualizados) y el gate se oculta sin
    /// navegar. CalendarioPagosViewModel se registra AddTransient y se recrea en cada navegación a
    /// la pantalla.
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

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoCalendario = "No tenés permiso para ver el calendario de pagos.";

    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor no se muestra el calendario anterior: al reintentar, las secciones no pueden quedar con datos viejos.</summary>
    protected override void AlPerderConexion()
    {
        Vencidas.Clear();
        AVencer7Dias.Clear();
        AVencer30Dias.Clear();
        PagosRecientes.Clear();
    }

    /// <summary>
    /// La dispara la View (DataContextChanged) y "Actualizar". Va por EjecutarCargaProtegidaAsync: un 403 deja SinPermiso
    /// (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del modal global, y mientras
    /// espera Cargando está en true. Pide turno: si mientras espera empezó otra carga, su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var calendario = await _service.ObtenerCalendarioPagosAsync();
            if (!turno.EsLaUltima)
                return;
            Reemplazar(Vencidas, calendario.Vencidas);
            Reemplazar(AVencer7Dias, calendario.AVencer7Dias);
            Reemplazar(AVencer30Dias, calendario.AVencer30Dias);
            Reemplazar(PagosRecientes, calendario.PagosRecientes);
        }, MensajeSinPermisoCalendario, turno);
    }

    private static void Reemplazar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var item in origen)
            destino.Add(item);
    }

    public IAsyncRelayCommand RecargarCommand => field ??= ComandoDeGuardado(RecargarAsync);

    private async Task RecargarAsync() => await CargarAsync();

    [RelayCommand]
    private async Task RegistrarPagoAsync(FacturaCalendarioDto? fila)
    {
        if (fila is null) return;
        var gasto = await _gastoService.ObtenerPorIdAsync(fila.GastoId);
        _navigation.Navegar<PagosGastoViewModel>(vm =>
        {
            vm.CargarParaGasto(gasto);
            vm.ConfigurarVolver(() => _navigation.Navegar<CalendarioPagosViewModel>(), "Calendario de pagos");
        });
    }
}
