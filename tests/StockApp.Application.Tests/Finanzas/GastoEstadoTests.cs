using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

/// <summary>
/// El estado de la factura NUNCA se persiste: se calcula de sum(pagos activos) vs
/// MontoTotal + FechaVencimiento + Activo (spec Finanzas §4, "Enums nuevos").
/// </summary>
public class GastoEstadoTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto GastoCredito(decimal monto, DateTime vencimiento) => new()
    {
        Detalle = "Compra de prueba",
        MontoTotal = monto,
        Fecha = Hoy.AddDays(-30),
        CondicionPago = CondicionPago.Credito,
        FechaVencimiento = vencimiento,
    };

    [Fact]
    public void SinPagos_CreditoNoVencido_Pendiente()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));

        Assert.Equal(EstadoGasto.Pendiente, gasto.CalcularEstado(Hoy));
        Assert.Equal(0m, gasto.TotalPagado);
        Assert.Equal(1000m, gasto.SaldoPendiente);
    }

    [Fact]
    public void PagoParcial_Parcial()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 400m });

        Assert.Equal(EstadoGasto.Parcial, gasto.CalcularEstado(Hoy));
        Assert.Equal(600m, gasto.SaldoPendiente);
    }

    [Fact]
    public void PagosCubrenElTotal_Pagada()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 400m });
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 600m });

        Assert.Equal(EstadoGasto.Pagada, gasto.CalcularEstado(Hoy));
        Assert.Equal(0m, gasto.SaldoPendiente);
    }

    [Fact]
    public void PagosAnulados_NoCuentan()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 1000m, Activo = false });

        Assert.Equal(EstadoGasto.Pendiente, gasto.CalcularEstado(Hoy));
        Assert.Equal(0m, gasto.TotalPagado);
    }

    [Fact]
    public void CreditoVencidoSinCubrir_Vencida_InclusoConPagoParcial()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(-1));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-5), Monto = 400m });

        Assert.Equal(EstadoGasto.Vencida, gasto.CalcularEstado(Hoy));
    }

    [Fact]
    public void CreditoVencidoPeroPagado_Pagada()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(-1));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-5), Monto = 1000m });

        Assert.Equal(EstadoGasto.Pagada, gasto.CalcularEstado(Hoy));
    }

    [Fact]
    public void ContadoNuncaVence_SinPagosEsPendiente()
    {
        // Caso teórico (el contado se crea con pago automático), pero el cálculo
        // no debe marcar Vencida sin FechaVencimiento.
        var gasto = new Gasto
        {
            Detalle = "Contado",
            MontoTotal = 500m,
            Fecha = Hoy.AddDays(-90),
            CondicionPago = CondicionPago.Contado,
        };

        Assert.Equal(EstadoGasto.Pendiente, gasto.CalcularEstado(Hoy));
    }

    [Fact]
    public void GastoInactivo_Anulada_SinImportarPagos()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(-1));
        gasto.Activo = false;

        Assert.Equal(EstadoGasto.Anulada, gasto.CalcularEstado(Hoy));
    }
}
