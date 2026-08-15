using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Pagos de la factura": lista los pagos (activos y anulados) de un gasto,
/// permite registrar un pago nuevo (sin superar el saldo — lo valida el servidor) y
/// anular pagos existentes. Refresca el gasto tras cada operación para que saldo y
/// estado calculado queden al día.
/// </summary>
public partial class PagosGastoViewModel : ViewModelBase
{
    private readonly IGastoService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;
    private readonly AdjuntosPanelViewModel _adjuntosPanel;

    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    /// <summary>
    /// Gatea "Registrar pago" y "Anular pago" (bugfix 2026-08-15): ninguno de los dos tenía
    /// gating por permiso — GastoService.RegistrarPagoAsync/AnularPagoAsync exigen
    /// Permisos.RegistrarPagos sin condición, así que un Operador con VerFinanzas (alcanza para
    /// entrar a esta pantalla) pero sin RegistrarPagos llenaba el formulario y recién al guardar
    /// se comía un 403. Esta pantalla es alcanzable por DOS caminos (GastosView, ya gateado, y
    /// CalendarioPagosView, sin gatear) — el gating va acá porque acá vive la ACCIÓN, cubriendo
    /// ambos caminos de navegación por definición. Mismo patrón que
    /// GastosViewModel.PuedeRegistrarPagos / DocumentoFormViewModel.PuedeAnular: se OCULTA
    /// (IsVisible), nunca se deshabilita. Sin refresco en caliente: PagosGastoViewModel se
    /// registra AddTransient y se recrea en cada navegación, toma el ICurrentSession vigente.
    /// </summary>
    public bool PuedeRegistrarPagos =>
        _session.RolActual == RolUsuario.Admin ||
        _session.PermisosActuales.Contains(Permisos.RegistrarPagos);

    private int _gastoId;
    private Action _volver;

    /// <summary>Cultura FIJA es-UY (patrón MonedaConverter).</summary>
    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }

    [ObservableProperty] private string _tituloGasto = string.Empty;
    [ObservableProperty] private decimal _montoTotal;
    [ObservableProperty] private decimal _totalPagado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private string _estado = string.Empty;

    [ObservableProperty] private DateTime? _fechaSeleccionada = DateTime.Today;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty] private string? _nota;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty] private PagoGasto? _pagoSeleccionado;

    public ObservableCollection<PagoGasto> Pagos { get; } = new();

    public PagosGastoViewModel(
        IGastoService service,
        ICurrentSession session,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        AdjuntosPanelViewModel adjuntosPanel)
    {
        _service       = service;
        _session       = session;
        _navigation    = navigation;
        _confirmacion  = confirmacion;
        _adjuntosPanel = adjuntosPanel;
        _volver        = () => _navigation.Navegar<GastosViewModel>();
    }

    // Fire-and-forget consciente: el panel de adjuntos se carga async sin bloquear
    // la selección del pago en la lista (mismo patrón que GastoFormViewModel).
    partial void OnPagoSeleccionadoChanged(PagoGasto? value)
    {
        if (value is not null)
            _ = _adjuntosPanel.InicializarAsync(null, value.Id);
    }

    /// <summary>Recibe el gasto de la grilla. Corre ANTES de InicializarAsync.</summary>
    public void CargarParaGasto(Gasto gasto) => _gastoId = gasto.Id;

    /// <summary>
    /// Permite que el ORIGEN de la navegación configure a dónde vuelve <see cref="Volver"/>.
    /// Por defecto (si no se llama) vuelve a <see cref="GastosViewModel"/>.
    /// </summary>
    public void ConfigurarVolver(Action volver) => _volver = volver;

    /// <summary>Trae el gasto fresco del servidor. La dispara la View (DataContextChanged).</summary>
    public async Task InicializarAsync()
    {
        try
        {
            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private async Task RefrescarAsync()
    {
        var gasto = await _service.ObtenerPorIdAsync(_gastoId);

        TituloGasto = $"{gasto.Detalle} — factura {gasto.NumeroFactura ?? "s/n"} " +
                      $"({gasto.Proveedor?.Nombre ?? $"proveedor {gasto.ProveedorId}"})";
        MontoTotal     = gasto.MontoTotal;
        TotalPagado    = gasto.TotalPagado;
        SaldoPendiente = gasto.SaldoPendiente;
        Estado         = gasto.CalcularEstado(DateTime.UtcNow).ToString();

        Pagos.Clear();
        foreach (var pago in gasto.Pagos)
            Pagos.Add(pago);
    }

    private bool PuedeRegistrar() => !string.IsNullOrWhiteSpace(MontoTexto);

    [RelayCommand(CanExecute = nameof(PuedeRegistrar))]
    private async Task RegistrarPagoAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto del pago no es un número válido.";
            return;
        }
        if (FechaSeleccionada is null)
        {
            MensajeError = "La fecha del pago es obligatoria.";
            return;
        }

        try
        {
            await _service.RegistrarPagoAsync(new PagoGasto
            {
                GastoId = _gastoId,
                Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
                Monto = monto,
                Nota = string.IsNullOrWhiteSpace(Nota) ? null : Nota,
            });

            MontoTexto = string.Empty;
            Nota = null;
            await RefrescarAsync();
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AnularPagoAsync(PagoGasto pago)
    {
        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma anular el pago de {pago.Monto.ToString("N2", CulturaMonto)} del {pago.Fecha:dd/MM/yyyy}?");
        if (!confirmar) return;

        try
        {
            await _service.AnularPagoAsync(_gastoId, pago.Id);
            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private void Volver() => _volver();
}
