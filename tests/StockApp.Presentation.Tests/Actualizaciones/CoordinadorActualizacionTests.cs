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

    // ── AplicarActualizacionAsync (wiring de los botones de overlay) ───────────

    [Fact]
    public async Task AplicarActualizacionAsync_FlujoOk_LlamaDescargarYLuegoAplicarYReiniciar()
    {
        var updateMock = new Mock<IUpdateService>();
        var orden = new MockSequence();
        updateMock
            .InSequence(orden)
            .Setup(s => s.DescargarAsync(null, default))
            .Returns(Task.CompletedTask);
        updateMock
            .InSequence(orden)
            .Setup(s => s.AplicarYReiniciar());

        var coordinador = Crear(updateMock);

        var resultado = await coordinador.AplicarActualizacionAsync();

        Assert.True(resultado);
        updateMock.Verify(s => s.DescargarAsync(null, default), Times.Once);
        updateMock.Verify(s => s.AplicarYReiniciar(), Times.Once);
    }

    [Fact]
    public async Task AplicarActualizacionAsync_DescargaFalla_NoPropagaYDevuelveFalse()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.DescargarAsync(null, default))
            .ThrowsAsync(new Exception("Sin conexión"));

        var coordinador = Crear(updateMock);

        var exception = await Record.ExceptionAsync(() => coordinador.AplicarActualizacionAsync());
        Assert.Null(exception);

        var resultado = await coordinador.AplicarActualizacionAsync();
        Assert.False(resultado);
        updateMock.Verify(s => s.AplicarYReiniciar(), Times.Never);
    }

    [Fact]
    public async Task AplicarActualizacionAsync_AplicarYReiniciarFalla_NoPropagaYDevuelveFalse()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock
            .Setup(s => s.DescargarAsync(null, default))
            .Returns(Task.CompletedTask);
        updateMock
            .Setup(s => s.AplicarYReiniciar())
            .Throws(new InvalidOperationException("No hay update descargado."));

        var coordinador = Crear(updateMock);

        var resultado = await coordinador.AplicarActualizacionAsync();

        Assert.False(resultado);
    }

    // ── evaluación en sesión (chequeo periódico) ────────────────────────────────

    private static UpdateCheckResult Hay(string version, UpdateSeverity severity) =>
        new(HayUpdate: true, Version: version, Severity: severity, NotasMarkdown: "notas");

    private static Mock<IUpdateService> ConBusqueda(UpdateCheckResult resultado)
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock.Setup(s => s.BuscarAsync(default)).ReturnsAsync(resultado);
        return updateMock;
    }

    [Fact]
    public async Task EvaluarEnSesion_MismaVersionDosVeces_NotificaUnaSola()
    {
        var coordinador = Crear(ConBusqueda(Hay("0.5.0", UpdateSeverity.Important)));

        var primera = await coordinador.EvaluarEnSesionAsync();
        var segunda = await coordinador.EvaluarEnSesionAsync();

        Assert.NotNull(primera);
        Assert.Equal(ModoUx.BannerDiscreto, primera!.Modo);
        Assert.Null(segunda);
    }

    [Fact]
    public async Task EvaluarEnSesion_VersionMostradaEnElArranque_NoSeRepite()
    {
        var coordinador = Crear(ConBusqueda(Hay("0.5.0", UpdateSeverity.Important)));

        await coordinador.EvaluarEnArranqueAsync();
        var enSesion = await coordinador.EvaluarEnSesionAsync();

        Assert.Null(enSesion);
    }

    [Fact]
    public async Task EvaluarEnSesion_BuscarTira_DevuelveNullSinPropagar()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock.Setup(s => s.BuscarAsync(default))
            .ThrowsAsync(new HttpRequestException("sin red"));
        var coordinador = Crear(updateMock);

        var accion = await coordinador.EvaluarEnSesionAsync();

        Assert.Null(accion);
    }

    [Fact]
    public async Task EvaluarEnSesion_ConArranqueEnCurso_SalteaElTickSinBuscar()
    {
        var gate = new TaskCompletionSource<UpdateCheckResult>();
        var updateMock = new Mock<IUpdateService>();
        updateMock.Setup(s => s.BuscarAsync(default)).Returns(gate.Task);
        var coordinador = Crear(updateMock);

        var arranque = coordinador.EvaluarEnArranqueAsync();   // toma el cerrojo y queda en BuscarAsync

        // WaitAsync: si el cerrojo se rompe, el tick se cuelga; que falle por timeout y no congele el CI.
        var accion = await coordinador.EvaluarEnSesionAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(accion);
        updateMock.Verify(s => s.BuscarAsync(default), Times.Once);

        gate.SetResult(UpdateCheckResult.SinUpdate);
        await arranque;
    }

    [Fact]
    public async Task EvaluarEnSesion_ConAplicacionEnCurso_NoBusca()
    {
        var gate = new TaskCompletionSource();
        var updateMock = ConBusqueda(Hay("0.5.0", UpdateSeverity.Important));
        updateMock.Setup(s => s.DescargarAsync(null, default)).Returns(gate.Task);
        var coordinador = Crear(updateMock);

        var aplicar = coordinador.AplicarActualizacionAsync();   // queda en DescargarAsync

        var accion = await coordinador.EvaluarEnSesionAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(accion);
        updateMock.Verify(s => s.BuscarAsync(default), Times.Never);

        gate.SetResult();
        Assert.True(await aplicar);
    }

    [Fact]
    public async Task EvaluarEnSesion_TrasAplicarExitoso_NoVuelveABuscar()
    {
        var updateMock = ConBusqueda(Hay("0.5.0", UpdateSeverity.Important));
        updateMock.Setup(s => s.DescargarAsync(null, default)).Returns(Task.CompletedTask);
        var coordinador = Crear(updateMock);

        Assert.True(await coordinador.AplicarActualizacionAsync());
        var accion = await coordinador.EvaluarEnSesionAsync();

        Assert.Null(accion);
        updateMock.Verify(s => s.BuscarAsync(default), Times.Never);
    }

    [Fact]
    public async Task EvaluarEnSesion_TrasAplicarFallido_LaVersionSePuedeVolverANotificar()
    {
        var updateMock = ConBusqueda(Hay("0.5.0", UpdateSeverity.Important));
        updateMock.Setup(s => s.DescargarAsync(null, default))
            .ThrowsAsync(new IOException("disco lleno"));
        var coordinador = Crear(updateMock);

        var primera = await coordinador.EvaluarEnSesionAsync();
        Assert.NotNull(primera);
        Assert.Null(await coordinador.EvaluarEnSesionAsync());

        Assert.False(await coordinador.AplicarActualizacionAsync());
        var tras = await coordinador.EvaluarEnSesionAsync();

        Assert.NotNull(tras);
        Assert.Equal("0.5.0", tras!.Version);
    }

    [Fact]
    public async Task EvaluarEnArranque_BuscarTira_LiberaElCerrojoYLaSesionSigueBuscando()
    {
        // Arrancar sin red es el camino real más probable: un cerrojo que quede tomado
        // silenciaría el chequeo periódico durante toda la sesión.
        var updateMock = new Mock<IUpdateService>();
        updateMock.SetupSequence(s => s.BuscarAsync(default))
            .ThrowsAsync(new HttpRequestException("sin red"))
            .ReturnsAsync(Hay("0.5.0", UpdateSeverity.Important));
        var coordinador = Crear(updateMock);

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinador.EvaluarEnArranqueAsync());

        var accion = await coordinador.EvaluarEnSesionAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(accion);
        Assert.Equal("0.5.0", accion!.Version);
        updateMock.Verify(s => s.BuscarAsync(default), Times.Exactly(2));
    }

    [Fact]
    public async Task EvaluarEnSesion_BuscarTira_LiberaElCerrojoYElSiguienteTickBusca()
    {
        var updateMock = new Mock<IUpdateService>();
        updateMock.SetupSequence(s => s.BuscarAsync(default))
            .ThrowsAsync(new HttpRequestException("sin red"))
            .ReturnsAsync(Hay("0.5.0", UpdateSeverity.Important));
        var coordinador = Crear(updateMock);

        Assert.Null(await coordinador.EvaluarEnSesionAsync());
        var accion = await coordinador.EvaluarEnSesionAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(accion);
        Assert.Equal("0.5.0", accion!.Version);
    }

    [Fact]
    public async Task EvaluarEnSesion_Critica_DevuelveAvisoCriticoEnSesion()
    {
        var coordinador = Crear(ConBusqueda(Hay("0.5.0", UpdateSeverity.Critical)));

        var accion = await coordinador.EvaluarEnSesionAsync();

        Assert.NotNull(accion);
        Assert.Equal(ModoUx.AvisoCriticoEnSesion, accion!.Modo);
    }
}
