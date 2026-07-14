using Microsoft.Extensions.Caching.Memory;
using Moq;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Infrastructure.Reportes;
using Xunit;

namespace StockApp.Infrastructure.Tests.Reportes;

public class ReporteStockServiceCacheadoTests
{
    private static ReporteStockServiceCacheado Crear(
        IReporteStockService inner, IVersionReportes version)
        => new(inner, new MemoryCache(new MemoryCacheOptions()), version);

    [Fact]
    public async Task Valorizacion_PrimeraLlamada_DelegaEnElInner()
    {
        var inner = new Mock<IReporteStockService>();
        var dto = new ValorizacionReporteDto(new List<ValorizacionItemDto>(), new ValorizacionTotalesDto(0, 0));
        inner.Setup(s => s.ObtenerValorizacionAsync()).ReturnsAsync(dto);
        var sut = Crear(inner.Object, new VersionReportes());

        var resultado = await sut.ObtenerValorizacionAsync();

        Assert.Same(dto, resultado);
        inner.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
    }

    [Fact]
    public async Task Valorizacion_SegundaLlamadaMismaVersion_SirveDeCacheSinTocarElInner()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(new List<ValorizacionItemDto>(), new ValorizacionTotalesDto(0, 0)));
        var sut = Crear(inner.Object, new VersionReportes());

        await sut.ObtenerValorizacionAsync();
        await sut.ObtenerValorizacionAsync();

        inner.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
    }

    [Fact]
    public async Task Valorizacion_TrasInvalidar_Recalcula()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(new List<ValorizacionItemDto>(), new ValorizacionTotalesDto(0, 0)));
        var version = new VersionReportes();
        var sut = Crear(inner.Object, version);

        await sut.ObtenerValorizacionAsync();
        version.Invalidar();
        await sut.ObtenerValorizacionAsync();

        inner.Verify(s => s.ObtenerValorizacionAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task MasMovidos_ParametrosDistintos_SonEntradasDistintas()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MasMovidoDto>());
        var sut = Crear(inner.Object, new VersionReportes());

        await sut.ObtenerMasMovidosAsync(new DateTime(2026, 1, 1), null, 20);
        await sut.ObtenerMasMovidosAsync(new DateTime(2026, 2, 1), null, 20);

        inner.Verify(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Historial_ProductosDistintos_SonEntradasDistintas()
    {
        var inner = new Mock<IReporteStockService>();
        inner.Setup(s => s.ObtenerHistorialPorProductoAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(new List<MovimientoHistorialDto>());
        var sut = Crear(inner.Object, new VersionReportes());

        await sut.ObtenerHistorialPorProductoAsync(1, null, null);
        await sut.ObtenerHistorialPorProductoAsync(2, null, null);

        inner.Verify(s => s.ObtenerHistorialPorProductoAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Exactly(2));
    }
}
