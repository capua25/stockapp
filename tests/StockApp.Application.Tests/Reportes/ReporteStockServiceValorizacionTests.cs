using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Reportes;

public class ReporteStockServiceValorizacionTests
{
    // ── helpers de setup ──────────────────────────────────────────────────────

    private static (ReporteStockService svc,
                    Mock<IReporteStockRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo        = new Mock<IReporteStockRepository>();
        var movimientos = new Mock<IMovimientoStockService>();
        var session     = new Mock<ICurrentSession>();
        var auth        = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);

        // Por defecto auth no lanza (permiso concedido)
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new ReporteStockService(
            repo.Object, movimientos.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
    }

    private static ValorizacionItemDto Item(
        int productoId,
        string categoria = "General",
        decimal valorCosto = 0m,
        decimal valorVenta = 0m) =>
        new ValorizacionItemDto(
            ProductoId:  productoId,
            Codigo:      $"P-{productoId:000}",
            Nombre:      $"Producto {productoId}",
            Categoria:   categoria,
            StockActual: 10m,
            PrecioCosto: 0m,
            PrecioVenta: 0m,
            ValorCosto:  valorCosto,
            ValorVenta:  valorVenta);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerValorizacionAsync_CalculaValorCostoYValorVenta_Correcto()
    {
        var (svc, repo, _, _) = Crear();
        var items = new[] { Item(1, valorCosto: 150m, valorVenta: 200m) };
        repo.Setup(r => r.ObtenerValorizacionAsync())
            .ReturnsAsync(items);

        var resultado = await svc.ObtenerValorizacionAsync();

        var item = Assert.Single(resultado.Items);
        Assert.Equal(150m, item.ValorCosto);
        Assert.Equal(200m, item.ValorVenta);
    }

    [Fact]
    public async Task ObtenerValorizacionAsync_CalculaTotalesCorrectamente()
    {
        var (svc, repo, _, _) = Crear();
        var items = new[]
        {
            Item(1, valorCosto: 100m, valorVenta: 150m),
            Item(2, valorCosto: 200m, valorVenta: 350m),
            Item(3, valorCosto:  50m, valorVenta:  75m),
        };
        repo.Setup(r => r.ObtenerValorizacionAsync())
            .ReturnsAsync(items);

        var resultado = await svc.ObtenerValorizacionAsync();

        Assert.Equal(350m, resultado.Totales.TotalValorCosto); // 100 + 200 + 50
        Assert.Equal(575m, resultado.Totales.TotalValorVenta); // 150 + 350 + 75
    }

    [Fact]
    public async Task ObtenerValorizacionAsync_ProductoSinCategoria_Retorna_SinCategoria()
    {
        var (svc, repo, _, _) = Crear();
        var items = new[] { Item(1, categoria: "Sin categoría") };
        repo.Setup(r => r.ObtenerValorizacionAsync())
            .ReturnsAsync(items);

        var resultado = await svc.ObtenerValorizacionAsync();

        var item = Assert.Single(resultado.Items);
        Assert.Equal("Sin categoría", item.Categoria);
    }

    [Fact]
    public async Task ObtenerValorizacionAsync_Operador_LanzaUnauthorizedAccessException()
    {
        var (svc, repo, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerValorizacionAsync());

        // Fail-closed: el repo NUNCA debe ser invocado.
        repo.Verify(r => r.ObtenerValorizacionAsync(), Times.Never);
    }
}
