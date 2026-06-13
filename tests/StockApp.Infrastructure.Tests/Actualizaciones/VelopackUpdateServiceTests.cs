using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Infrastructure.Actualizaciones;
using Velopack;

namespace StockApp.Infrastructure.Tests.Actualizaciones;

/// <summary>
/// Tests de VelopackUpdateService con gateway mockeado.
/// NotInstalledException se absorbe en VelopackGatewayReal (adaptador), no aquí:
/// el servicio solo ve UpdateInfo? y trata null como "sin update".
/// </summary>
public class VelopackUpdateServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static VelopackAsset BuildAsset(string version, string? notesMarkdown = null)
        => new VelopackAsset
        {
            Version = SemanticVersion.Parse(version),
            NotesMarkdown = notesMarkdown,
        };

    private static UpdateInfo BuildUpdateInfo(VelopackAsset asset)
        => new UpdateInfo(asset, isDowngrade: false);

    // ── BuscarAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_GatewayDevuelveNull_RetornaSinUpdate()
    {
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync((UpdateInfo?)null);

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.False(resultado.HayUpdate);
        Assert.Equal(UpdateCheckResult.SinUpdate, resultado);
    }

    [Fact]
    public async Task BuscarAsync_UpdateConNotesNulas_SeverityNormal()
    {
        // Simula gateway que devuelve null en NotesMarkdown (notas vacías)
        var asset = BuildAsset("2.0.0", notesMarkdown: null);
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(BuildUpdateInfo(asset));

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.True(resultado.HayUpdate);
        Assert.Equal(UpdateSeverity.Normal, resultado.Severity);
        Assert.Equal("2.0.0", resultado.Version);
    }

    [Fact]
    public async Task BuscarAsync_UpdateSinFrontMatter_SeverityNormal()
    {
        var asset = BuildAsset("2.0.0", "## Cambios menores\n- fix bug");
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(BuildUpdateInfo(asset));

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.True(resultado.HayUpdate);
        Assert.Equal(UpdateSeverity.Normal, resultado.Severity);
    }

    [Fact]
    public async Task BuscarAsync_UpdateImportant_SeverityImportant()
    {
        var asset = BuildAsset("2.1.0", "severity: important\n## Correcciones de seguridad");
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(BuildUpdateInfo(asset));

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.True(resultado.HayUpdate);
        Assert.Equal(UpdateSeverity.Important, resultado.Severity);
    }

    [Fact]
    public async Task BuscarAsync_UpdateCritical_SeverityCritical()
    {
        var asset = BuildAsset("3.0.0", "severity: critical\n## Parche urgente");
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(BuildUpdateInfo(asset));

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.True(resultado.HayUpdate);
        Assert.Equal(UpdateSeverity.Critical, resultado.Severity);
    }

    [Fact]
    public async Task BuscarAsync_AppNoInstalada_RetornaSinUpdateSinLlamarGateway()
    {
        // EstaInstalado=false → el servicio cortocircuita sin llamar al gateway
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(false);

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.False(resultado.HayUpdate);
        gateway.Verify(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuscarAsync_GatewayDevuelveNullCuandoNoInstalado_RetornaSinUpdate()
    {
        // Simula el caso donde el gateway ya absorbió NotInstalledException y devolvió null
        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync((UpdateInfo?)null);

        var sut = new VelopackUpdateService(gateway.Object);
        var resultado = await sut.BuscarAsync();

        Assert.False(resultado.HayUpdate);
        Assert.Equal(UpdateCheckResult.SinUpdate, resultado);
    }

    // ── DescargarAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DescargarAsync_ConUpdateGuardado_LlamaGatewayConUpdateInfo()
    {
        var asset = BuildAsset("2.0.0");
        var updateInfo = BuildUpdateInfo(asset);

        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(updateInfo);
        gateway.Setup(g => g.DescargarUpdateAsync(updateInfo, It.IsAny<Action<int>?>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var sut = new VelopackUpdateService(gateway.Object);
        await sut.BuscarAsync();
        await sut.DescargarAsync();

        gateway.Verify(g => g.DescargarUpdateAsync(updateInfo, It.IsAny<Action<int>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DescargarAsync_SinBuscarPrimero_LanzaInvalidOperationException()
    {
        var gateway = new Mock<IVelopackGateway>();
        var sut = new VelopackUpdateService(gateway.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.DescargarAsync());
    }

    [Fact]
    public async Task DescargarAsync_ProgresoEsPropagado_AlGateway()
    {
        var asset = BuildAsset("2.0.0");
        var updateInfo = BuildUpdateInfo(asset);
        int? porcentajeRecibido = null;

        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(updateInfo);
        gateway.Setup(g => g.DescargarUpdateAsync(updateInfo, It.IsAny<Action<int>?>(), It.IsAny<CancellationToken>()))
               .Callback<UpdateInfo, Action<int>?, CancellationToken>((_, prog, _) => prog?.Invoke(50))
               .Returns(Task.CompletedTask);

        var sut = new VelopackUpdateService(gateway.Object);
        await sut.BuscarAsync();
        await sut.DescargarAsync(new Progress<UpdateProgress>(p => porcentajeRecibido = p.Porcentaje));

        Assert.Equal(50, porcentajeRecibido);
    }

    // ── AplicarYReiniciar ────────────────────────────────────────────────────

    [Fact]
    public async Task AplicarYReiniciar_ConUpdateGuardado_LlamaGateway()
    {
        var asset = BuildAsset("2.0.0");
        var updateInfo = BuildUpdateInfo(asset);

        var gateway = new Mock<IVelopackGateway>();
        gateway.Setup(g => g.EstaInstalado).Returns(true);
        gateway.Setup(g => g.BuscarUpdateAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(updateInfo);

        var sut = new VelopackUpdateService(gateway.Object);
        await sut.BuscarAsync();
        sut.AplicarYReiniciar();

        gateway.Verify(g => g.AplicarYReiniciar(updateInfo), Times.Once);
    }

    [Fact]
    public void AplicarYReiniciar_SinUpdateGuardado_LanzaInvalidOperationException()
    {
        var gateway = new Mock<IVelopackGateway>();
        var sut = new VelopackUpdateService(gateway.Object);

        var ex = Record.Exception(() => sut.AplicarYReiniciar());
        Assert.IsType<InvalidOperationException>(ex);
    }
}
