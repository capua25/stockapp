using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
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
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    private int _gastoId;

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

    [ObservableProperty] private DateTimeOffset? _fechaSeleccionada = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty] private string? _nota;
    [ObservableProperty] private string? _mensajeError;

    public ObservableCollection<PagoGasto> Pagos { get; } = new();

    public PagosGastoViewModel(
        IGastoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    /// <summary>Recibe el gasto de la grilla. Corre ANTES de InicializarAsync.</summary>
    public void CargarParaGasto(Gasto gasto) => _gastoId = gasto.Id;

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
    private void Volver() => _navigation.Navegar<GastosViewModel>();
}
