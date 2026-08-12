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

    [Fact]
    public async Task IConfirmacionService_InformarAsync_EsMockeable()
    {
        var mock = new Mock<IConfirmacionService>();
        mock.Setup(s => s.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        await mock.Object.InformarAsync("Ocurrió un error.");

        mock.Verify(s => s.InformarAsync("Ocurrió un error."), Times.Once);
    }

    [Fact]
    public async Task ConfirmacionService_InformarAsync_SinAppAvalonia_NoLanza()
    {
        // Sin Avalonia.Application.Current inicializado (tests headless), InformarAsync debe
        // resolver sin excepción en vez de intentar abrir un diálogo real — mismo criterio
        // defensivo que PreguntarAsync.
        var svc = new ConfirmacionService();

        await svc.InformarAsync("Ocurrió un error.");
    }

    [Fact]
    public async Task IConfirmacionService_PedirTextoAsync_EsMockeable()
    {
        var mock = new Mock<IConfirmacionService>();
        mock.Setup(s => s.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Motivo tipeado");

        var resultado = await mock.Object.PedirTextoAsync("Anular documento", "Ingrese el motivo");

        Assert.Equal("Motivo tipeado", resultado);
        mock.Verify(s => s.PedirTextoAsync("Anular documento", "Ingrese el motivo"), Times.Once);
    }

    [Fact]
    public async Task ConfirmacionService_PedirTextoAsync_SinAppAvalonia_DevuelveNull()
    {
        // Mismo criterio defensivo que PreguntarAsync/InformarAsync: sin Avalonia.Application.Current
        // inicializado (tests headless), rechaza de forma segura devolviendo null (equivale a cancelar).
        var svc = new ConfirmacionService();

        var resultado = await svc.PedirTextoAsync("Anular documento", "Ingrese el motivo");

        Assert.Null(resultado);
    }
}
