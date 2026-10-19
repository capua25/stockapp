using StockApp.Presentation.Actualizaciones;
using Xunit;

namespace StockApp.Presentation.Tests.Actualizaciones;

public class UrlFeedActualizacionesTests
{
    [Theory]
    [InlineData("https://h:8080", "https://h:8080/updates/")]
    [InlineData("https://h:8080/", "https://h:8080/updates/")]
    [InlineData("http://localhost:5043", "http://localhost:5043/updates/")]
    [InlineData("https://h/api", "https://h/api/updates/")]
    [InlineData("https://h/api/", "https://h/api/updates/")]
    public void Desde_DerivaElFeedDeLaUrlDeLaApi(string apiBaseUrl, string esperado)
    {
        Assert.Equal(esperado, UrlFeedActualizaciones.Desde(apiBaseUrl));
    }
}
