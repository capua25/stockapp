using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Presentation.Actualizaciones;
using Xunit;

namespace StockApp.Presentation.Tests.Actualizaciones;

public class CoordinadorActualizacionTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static CoordinadorActualizacion Crear(Mock<IUpdateService> updateMock)
        => new(updateMock.Object, new PoliticaUxActualizacion());

    // ── C2.1 tests (rojo primero) ─────────────────────────────────────────────

    [Fact]
    public async Task Evaluar_SinUpdate_AccionUxEsNinguno()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(UpdateCheckResult.SinUpdate);

        var coordinador = Crear(updateMock);

        await coordinador.EvaluarEnArranqueAsync();

        Assert.Equal(ModoUx.Ninguno, coordinador.AccionUxActual.Modo);
    }

    [Fact]
    public async Task Evaluar_Normal_AccionUxEsBannerDiscreto()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(new UpdateCheckResult(
                HayUpdate: true,
                Version: "1.1.0",
                Severity: UpdateSeverity.Normal,
                NotasMarkdown: "severity: normal\n\nCambios menores."));

        var coordinador = Crear(updateMock);

        await coordinador.EvaluarEnArranqueAsync();

        Assert.Equal(ModoUx.BannerDiscreto, coordinador.AccionUxActual.Modo);
    }

    [Fact]
    public async Task Evaluar_CriticalDescargaOk_AccionUxEsBloqueoCritico()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(new UpdateCheckResult(
                HayUpdate: true,
                Version: "1.1.0",
                Severity: UpdateSeverity.Critical,
                NotasMarkdown: "severity: critical\n\nActualización urgente."));
        updateMock
            .Setup(s => s.DescargarAsync(null, default))
            .Returns(Task.CompletedTask);

        var coordinador = Crear(updateMock);

        await coordinador.EvaluarEnArranqueAsync();

        Assert.Equal(ModoUx.BloqueoCritico, coordinador.AccionUxActual.Modo);
    }

    [Fact]
    public async Task Evaluar_CriticalDescargaFalla_AccionUxEsModoDegradado()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.BuscarAsync(default))
            .ReturnsAsync(new UpdateCheckResult(
                HayUpdate: true,
                Version: "1.1.0",
                Severity: UpdateSeverity.Critical,
                NotasMarkdown: "severity: critical\n\nActualización urgente."));
        updateMock
            .Setup(s => s.DescargarAsync(null, default))
            .ThrowsAsync(new Exception("Sin conexión"));

        var coordinador = Crear(updateMock);

        await coordinador.EvaluarEnArranqueAsync();

        Assert.Equal(ModoUx.ModoDegradado, coordinador.AccionUxActual.Modo);
    }
}
