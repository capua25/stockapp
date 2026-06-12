using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Reportes;

public class ReporteStockServiceCategoriaTests
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

    private static StockCategoriaDto Cat(
        string categoria,
        int cantidadProductos = 1,
        decimal stockTotal = 0m,
        decimal valorCosto = 0m,
        decimal valorVenta = 0m) =>
        new StockCategoriaDto(
            Categoria:          categoria,
            CantidadProductos:  cantidadProductos,
            StockTotal:         stockTotal,
            ValorCosto:         valorCosto,
            ValorVenta:         valorVenta);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerStockPorCategoriaAsync_AgrupaCorrectamente()
    {
        var (svc, repo, _, _) = Crear();
        var grupos = new[]
        {
            Cat("Bebidas",   cantidadProductos: 3, stockTotal: 30m, valorCosto: 300m, valorVenta: 450m),
            Cat("Lácteos",   cantidadProductos: 2, stockTotal: 12m, valorCosto: 120m, valorVenta: 180m),
            Cat("Limpieza",  cantidadProductos: 5, stockTotal: 50m, valorCosto: 500m, valorVenta: 750m),
        };
        repo.Setup(r => r.ObtenerStockPorCategoriaAsync())
            .ReturnsAsync(grupos);

        var result = await svc.ObtenerStockPorCategoriaAsync();

        // Passthrough fiel: el agrupamiento real es del repo (C2).
        Assert.Equal(3, result.Count);
        Assert.Same(grupos[0], result[0]);
        Assert.Same(grupos[1], result[1]);
        Assert.Same(grupos[2], result[2]);
    }

    [Fact]
    public async Task ObtenerStockPorCategoriaAsync_NullCategoria_GrupoSinCategoria()
    {
        var (svc, repo, _, _) = Crear();
        var grupos = new[]
        {
            Cat("General"),
            Cat("Sin categoría", cantidadProductos: 4, stockTotal: 40m),
        };
        repo.Setup(r => r.ObtenerStockPorCategoriaAsync())
            .ReturnsAsync(grupos);

        var result = await svc.ObtenerStockPorCategoriaAsync();

        // El grupo "Sin categoría" lo resuelve el repo; el service hace passthrough.
        var sinCategoria = Assert.Single(result, g => g.Categoria == "Sin categoría");
        Assert.Equal(4, sinCategoria.CantidadProductos);
        Assert.Equal(40m, sinCategoria.StockTotal);
    }

    [Fact]
    public async Task ObtenerStockPorCategoriaAsync_Operador_LanzaUnauthorized()
    {
        var (svc, repo, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerStockPorCategoriaAsync());

        // Fail-closed: el repo NUNCA debe ser invocado.
        repo.Verify(r => r.ObtenerStockPorCategoriaAsync(), Times.Never);
    }
}
