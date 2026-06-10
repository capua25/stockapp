using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// D1 — Verifica que IConfirmacionService es mockeable con Moq (interfaz existe y es correcta).
/// </summary>
public class ConfirmacionServiceTests
{
    [Fact]
    public async Task IConfirmacionService_EsMockeable_YDevuelveValorConfigurable()
    {
        var mock = new Mock<IConfirmacionService>();
        mock.Setup(s => s.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var resultado = await mock.Object.PreguntarAsync("¿Querés continuar?");

        Assert.True(resultado);
        mock.Verify(s => s.PreguntarAsync("¿Querés continuar?"), Times.Once);
    }

    [Fact]
    public async Task ConfirmacionService_PreguntarAsync_DevuelveFalsePorDefecto()
    {
        // El stub devuelve false por defecto hasta que se conecte la View real
        var svc = new ConfirmacionService();

        var resultado = await svc.PreguntarAsync("¿Confirmar salida?");

        Assert.False(resultado);
    }
}
