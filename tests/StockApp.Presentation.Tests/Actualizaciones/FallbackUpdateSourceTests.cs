using Moq;
using StockApp.Presentation.Actualizaciones;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace StockApp.Presentation.Tests.Actualizaciones;

/// <summary>
/// Tests de FallbackUpdateSource: fuente encadenada [primaria → fallback].
/// Si la primaria falla, se intenta el fallback. Si el fallback también falla, se propaga.
/// </summary>
public class FallbackUpdateSourceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static VelopackAssetFeed BuildFeed()
        => new VelopackAssetFeed { Assets = Array.Empty<VelopackAsset>() };

    private static (Mock<IUpdateSource> primaria, Mock<IUpdateSource> fallback, FallbackUpdateSource sut) Build()
    {
        var primaria = new Mock<IUpdateSource>();
        var fallback = new Mock<IUpdateSource>();
        var sut = new FallbackUpdateSource(new[] { primaria.Object, fallback.Object });
        return (primaria, fallback, sut);
    }

    private static IVelopackLogger? NullLogger => null;

    // ── GetReleaseFeed ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetReleaseFeed_PrimariaOk_UsaPrimaria()
    {
        var (primaria, fallback, sut) = Build();
        var feed = BuildFeed();

        primaria.Setup(s => s.GetReleaseFeed(
                    It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()))
                .ReturnsAsync(feed);

        var resultado = await sut.GetReleaseFeed(NullLogger!, "app", "stable", null, null!);

        Assert.Same(feed, resultado);
        fallback.Verify(s => s.GetReleaseFeed(
            It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()), Times.Never);
    }

    [Fact]
    public async Task GetReleaseFeed_PrimariaFalla_UsaFallback()
    {
        var (primaria, fallback, sut) = Build();
        var feed = BuildFeed();

        primaria.Setup(s => s.GetReleaseFeed(
                    It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()))
                .ThrowsAsync(new HttpRequestException("unreachable"));

        fallback.Setup(s => s.GetReleaseFeed(
                    It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()))
                .ReturnsAsync(feed);

        var resultado = await sut.GetReleaseFeed(NullLogger!, "app", "stable", null, null!);

        Assert.Same(feed, resultado);
        fallback.Verify(s => s.GetReleaseFeed(
            It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()), Times.Once);
    }

    [Fact]
    public async Task GetReleaseFeed_TodasFallan_PropagaUltimaExcepcion()
    {
        var (primaria, fallback, sut) = Build();

        primaria.Setup(s => s.GetReleaseFeed(
                    It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()))
                .ThrowsAsync(new HttpRequestException("primaria caída"));

        fallback.Setup(s => s.GetReleaseFeed(
                    It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()))
                .ThrowsAsync(new HttpRequestException("fallback caído"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.GetReleaseFeed(NullLogger!, "app", "stable", null, null!));
    }

    // ── DownloadReleaseEntry ─────────────────────────────────────────────────

    [Fact]
    public async Task DownloadReleaseEntry_PrimariaOk_UsaPrimaria()
    {
        var (primaria, fallback, sut) = Build();
        var asset = new VelopackAsset { FileName = "pkg.nupkg" };

        primaria.Setup(s => s.DownloadReleaseEntry(
                    It.IsAny<IVelopackLogger>(), asset,
                    It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        await sut.DownloadReleaseEntry(NullLogger!, asset, "/tmp/pkg.nupkg", _ => { }, CancellationToken.None);

        primaria.Verify(s => s.DownloadReleaseEntry(
            It.IsAny<IVelopackLogger>(), asset,
            It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()), Times.Once);
        fallback.Verify(s => s.DownloadReleaseEntry(
            It.IsAny<IVelopackLogger>(), It.IsAny<VelopackAsset>(),
            It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadReleaseEntry_PrimariaFalla_UsaFallback()
    {
        var (primaria, fallback, sut) = Build();
        var asset = new VelopackAsset { FileName = "pkg.nupkg" };

        primaria.Setup(s => s.DownloadReleaseEntry(
                    It.IsAny<IVelopackLogger>(), asset,
                    It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("primaria caída"));

        fallback.Setup(s => s.DownloadReleaseEntry(
                    It.IsAny<IVelopackLogger>(), asset,
                    It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        await sut.DownloadReleaseEntry(NullLogger!, asset, "/tmp/pkg.nupkg", _ => { }, CancellationToken.None);

        fallback.Verify(s => s.DownloadReleaseEntry(
            It.IsAny<IVelopackLogger>(), asset,
            It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadReleaseEntry_TodasFallan_PropagaUltimaExcepcion()
    {
        var (primaria, fallback, sut) = Build();
        var asset = new VelopackAsset { FileName = "pkg.nupkg" };

        primaria.Setup(s => s.DownloadReleaseEntry(
                    It.IsAny<IVelopackLogger>(), asset,
                    It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("primaria caída"));

        fallback.Setup(s => s.DownloadReleaseEntry(
                    It.IsAny<IVelopackLogger>(), asset,
                    It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("fallback caído"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            sut.DownloadReleaseEntry(NullLogger!, asset, "/tmp/pkg.nupkg", _ => { }, CancellationToken.None));
    }
}
