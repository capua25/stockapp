using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Reportes;

public class ReporteStockServiceHistorialTests
{
    // ── helpers de setup ──────────────────────────────────────────────────────

    private static (ReporteStockService svc,
                    Mock<IReporteStockRepository> repoMock,
                    Mock<IMovimientoStockService> movimientosMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo        = new Mock<IReporteStockRepository>();
        var movimientos = new Mock<IMovimientoStockService>();
        var session     = new Mock<ICurrentSession>();
        var auth        = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new ReporteStockService(
            repo.Object, movimientos.Object, session.Object, auth.Object);
        return (svc, repo, movimientos, session, auth);
    }

    private static MovimientoHistorialDto HistItem(int movimientoId, int productoId) =>
        new MovimientoHistorialDto(
            MovimientoId:    movimientoId,
            ProductoId:      productoId,
            ProductoNombre:  $"Producto {productoId}",
            Tipo:            TipoMovimiento.Entrada,
            Motivo:          MotivoMovimiento.Compra,
            Cantidad:        5m,
            PrecioUnitario:  10m,
            StockAnterior:   0m,
            StockNuevo:      5m,
            Comentario:      null,
            Fecha:           new DateTime(2026, 1, 1),
            UsuarioId:       1);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerHistorialPorProductoAsync_DelegaAMovimientoStockService()
    {
        var (svc, _, movimientos, _, _) = Crear();
        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);
        var esperado = new[] { HistItem(1, 42), HistItem(2, 42) };

        movimientos
            .Setup(m => m.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(esperado);

        var result = await svc.ObtenerHistorialPorProductoAsync(42, desde, hasta);

        // Devuelve exactamente lo que retorna el servicio de movimientos (D2).
        Assert.Same(esperado, result);

        // Delega con los parámetros correctos mapeados al filtro.
        movimientos.Verify(m => m.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.ProductoId == 42 &&
            f.FechaDesde == desde &&
            f.FechaHasta == hasta)), Times.Once);
    }

    [Fact]
    public async Task ObtenerHistorialPorProductoAsync_Operador_LanzaUnauthorized()
    {
        var (svc, _, movimientos, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerHistorialPorProductoAsync(42, null, null));

        // Fail-closed: el servicio de movimientos NUNCA debe ser invocado.
        movimientos.Verify(
            m => m.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()),
            Times.Never);
    }
}
