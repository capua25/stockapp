using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Sin paginación en el sistema (spec 2026-09-18), exportar una tabla sin filtro de fecha
/// puede generar miles de páginas. Se avisa y se pide confirmación por encima del umbral.
/// </summary>
public class AvisoVolumenExportacionTests
{
    [Fact]
    public async Task ConfirmarAsync_ConExactamenteElUmbral_NoPregunta()
    {
        var confirmMock = new Mock<IConfirmacionService>();

        var resultado = await AvisoVolumenExportacion.ConfirmarAsync(
            AvisoVolumenExportacion.UmbralFilas, confirmMock.Object);

        Assert.True(resultado);
        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmarAsync_PorEncimaDelUmbral_PreguntaConLaCantidadDePaginasEstimadas()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var resultado = await AvisoVolumenExportacion.ConfirmarAsync(2000, confirmMock.Object);

        Assert.True(resultado);
        confirmMock.Verify(c => c.PreguntarAsync(It.Is<string>(
            m => m.Contains("2000") && m.Contains("50"))), Times.Once);
    }

    [Fact]
    public async Task ConfirmarAsync_PorEncimaDelUmbral_SiElUsuarioCancela_DevuelveFalse()
    {
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        var resultado = await AvisoVolumenExportacion.ConfirmarAsync(600, confirmMock.Object);

        Assert.False(resultado);
    }
}
