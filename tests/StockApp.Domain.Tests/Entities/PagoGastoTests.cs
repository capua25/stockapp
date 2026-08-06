using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class PagoGastoTests
{
    // Único punto de construcción del pago automático de contado (spec §4). Existe porque un
    // origen real (ImportacionRepository.ConfirmarAsync) armó el PagoGasto a mano y se olvidó de
    // EsAutomatico=true — el guard de anulación en cascada
    // (Gasto.PagosAutomaticosADarDeBajaEnAnulacion) lo trataba entonces como pago MANUAL. Este
    // test es la barrera contra un cuarto origen que cometa el mismo olvido: cualquier código
    // nuevo que necesite un pago automático debería pasar por acá.

    [Fact]
    public void Automatico_SiempreDevuelveEsAutomaticoTrue()
    {
        var pago = PagoGasto.Automatico(
            new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc), 1000m, "Pago contado (automático)");

        Assert.True(pago.EsAutomatico);
    }

    [Fact]
    public void Automatico_SeteaFechaMontoYNota()
    {
        var fecha = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);

        var pago = PagoGasto.Automatico(fecha, 1000m, "Pago contado (importación)");

        Assert.Equal(fecha, pago.Fecha);
        Assert.Equal(1000m, pago.Monto);
        Assert.Equal("Pago contado (importación)", pago.Nota);
        Assert.Null(pago.IdImportacion);
    }

    [Fact]
    public void Automatico_ConIdImportacion_LoAsigna()
    {
        var idImportacion = Guid.NewGuid();

        var pago = PagoGasto.Automatico(DateTime.UtcNow, 500m, "Pago contado (importación)", idImportacion);

        Assert.Equal(idImportacion, pago.IdImportacion);
        Assert.True(pago.EsAutomatico);
    }
}
