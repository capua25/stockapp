using StockApp.ApiClient;

namespace StockApp.ApiClient.Tests;

public class ServidorNoDisponibleExceptionTests
{
    [Fact]
    public void Constructor_SinInner_UsaElMensajePorDefecto()
    {
        var ex = new ServidorNoDisponibleException();

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_ConInner_PreservaLaCausa()
    {
        var causa = new HttpRequestException("connection refused");

        var ex = new ServidorNoDisponibleException(causa);

        Assert.Same(causa, ex.InnerException);
        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, ex.Message);
    }
}
