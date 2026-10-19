using Moq;
using StockApp.Presentation.Actualizaciones;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace StockApp.Presentation.Tests.Actualizaciones;

/// <summary>
/// Tests de FuenteFeedRegistrado: decorador que recuerda el feed que devolvió la fuente real.
/// </summary>
public class FuenteFeedRegistradoTests
{
    private static IVelopackLogger? NullLogger => null;

    [Fact]
    public void UltimoFeed_AntesDeConsultar_EstaVacio()
    {
        var sut = new FuenteFeedRegistrado(new Mock<IUpdateSource>().Object);

        Assert.Empty(sut.UltimoFeed);
    }

    [Fact]
    public async Task GetReleaseFeed_DevuelveElFeedDeLaInternaYLoRegistra()
    {
        var asset = new VelopackAsset { Version = SemanticVersion.Parse("0.4.0") };
        var feed = new VelopackAssetFeed { Assets = new[] { asset } };
        var interna = new Mock<IUpdateSource>();
        interna.Setup(s => s.GetReleaseFeed(
                    It.IsAny<IVelopackLogger>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<VelopackAsset>()))
               .ReturnsAsync(feed);
        var sut = new FuenteFeedRegistrado(interna.Object);

        var resultado = await sut.GetReleaseFeed(NullLogger!, "app", "stable", null, null!);

        Assert.Same(feed, resultado);
        Assert.Same(asset, Assert.Single(sut.UltimoFeed));
    }

    [Fact]
    public async Task DownloadReleaseEntry_DelegaEnLaInterna()
    {
        var asset = new VelopackAsset { FileName = "pkg.nupkg" };
        var interna = new Mock<IUpdateSource>();
        interna.Setup(s => s.DownloadReleaseEntry(
                    It.IsAny<IVelopackLogger>(), asset,
                    It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        var sut = new FuenteFeedRegistrado(interna.Object);

        await sut.DownloadReleaseEntry(NullLogger!, asset, "/tmp/pkg.nupkg", _ => { }, CancellationToken.None);

        interna.Verify(s => s.DownloadReleaseEntry(
            It.IsAny<IVelopackLogger>(), asset,
            It.IsAny<string>(), It.IsAny<Action<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
