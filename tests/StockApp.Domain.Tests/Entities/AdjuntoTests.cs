using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class AdjuntoTests
{
    [Fact]
    public void EsDeGasto_ConGastoIdSeteado_EsTrue()
    {
        var adjunto = new Adjunto { GastoId = 5 };

        Assert.True(adjunto.EsDeGasto);
        Assert.False(adjunto.EsDePago);
    }

    [Fact]
    public void EsDePago_ConPagoGastoIdSeteado_EsTrue()
    {
        var adjunto = new Adjunto { PagoGastoId = 8 };

        Assert.True(adjunto.EsDePago);
        Assert.False(adjunto.EsDeGasto);
    }

    [Fact]
    public void Activo_PorDefecto_EsTrue()
    {
        var adjunto = new Adjunto();

        Assert.True(adjunto.Activo);
    }
}
