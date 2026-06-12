using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Reportes;

public class ReporteStockServiceMasMovidosTests
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

    private static MasMovidoDto Mov(
        int productoId,
        int cantidadMovimientos = 1,
        decimal volumenTotal = 0m) =>
        new MasMovidoDto(
            ProductoId:          productoId,
            Codigo:              $"P-{productoId:000}",
            Nombre:              $"Producto {productoId}",
            CantidadMovimientos: cantidadMovimientos,
            VolumenTotal:        volumenTotal);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerMasMovidosAsync_OrdenadoPorVolumenTotalDesc()
    {
        var (svc, repo, _, _) = Crear();
        // El orden lo establece el repo (C3); el service hace passthrough fiel.
        var ordenados = new[]
        {
            Mov(3, volumenTotal: 300m),
            Mov(1, volumenTotal: 200m),
            Mov(2, volumenTotal: 100m),
        };
        repo.Setup(r => r.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(ordenados);

        var result = await svc.ObtenerMasMovidosAsync(null, null);

        Assert.Equal(3, result.Count);
        Assert.Same(ordenados[0], result[0]);
        Assert.Same(ordenados[1], result[1]);
        Assert.Same(ordenados[2], result[2]);
    }

    [Fact]
    public async Task ObtenerMasMovidosAsync_TopNRespetado()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<MasMovidoDto>());

        // topN explícito se pasa tal cual al repo.
        await svc.ObtenerMasMovidosAsync(null, null, topN: 5);
        repo.Verify(r => r.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 5), Times.Once);

        // default es 20 cuando no se especifica topN.
        await svc.ObtenerMasMovidosAsync(null, null);
        repo.Verify(r => r.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 20), Times.Once);
    }

    [Fact]
    public async Task ObtenerMasMovidosAsync_SinMovimientos_ListaVacia()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<MasMovidoDto>());

        var result = await svc.ObtenerMasMovidosAsync(null, null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ObtenerMasMovidosAsync_Operador_LanzaUnauthorized()
    {
        var (svc, repo, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerMasMovidosAsync(null, null));

        // Fail-closed: el repo NUNCA debe ser invocado.
        repo.Verify(
            r => r.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()),
            Times.Never);
    }
}
