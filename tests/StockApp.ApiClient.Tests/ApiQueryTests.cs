// tests/StockApp.ApiClient.Tests/ApiQueryTests.cs
using StockApp.ApiClient;

namespace StockApp.ApiClient.Tests;

public class ApiQueryTests
{
    [Fact]
    public void SinValores_DevuelveVacio()
    {
        Assert.Equal(string.Empty, ApiQuery.Construir(("sku", null), ("nombre", null)));
    }

    [Fact]
    public void OmiteNulosYConcatenaLosPresentes()
    {
        var query = ApiQuery.Construir(("sku", null), ("nombre", "coca"), ("codigoBarras", "779"));

        Assert.Equal("?nombre=coca&codigoBarras=779", query);
    }

    [Fact]
    public void EscapaLosValores()
    {
        var query = ApiQuery.Construir(("texto", "agua c/gas & más"));

        Assert.Equal("?texto=agua%20c%2Fgas%20%26%20m%C3%A1s", query);
    }

    [Fact]
    public void Fecha_UsaFormatoRoundTrip()
    {
        var fecha = new DateTime(2026, 7, 10, 14, 30, 0);

        Assert.Equal("2026-07-10T14:30:00.0000000", ApiQuery.Fecha(fecha));
        Assert.Null(ApiQuery.Fecha(null));
    }
}
