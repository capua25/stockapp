using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Movimientos;

public class MovimientoStockServiceInvalidacionTests
{
    [Fact]
    public async Task RegistrarAsync_Exitoso_InvalidaLaVersionDeReportes()
    {
        var repo    = new Mock<IMovimientoStockRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var version = new Mock<IVersionReportes>();

        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(1, "test-user", RolUsuario.Admin, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var producto = new Producto
        {
            Id = 1, Nombre = "Test", Codigo = "T-001",
            StockActual = 20m, Activo = true, UnidadMedidaId = 1
        };
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.Ok, 1, 25m));

        var svc = new MovimientoStockService(repo.Object, session.Object, auth.Object, version.Object);

        var dto = new RegistrarMovimientoDto(1, TipoMovimiento.Entrada, MotivoMovimiento.Compra,
                                             5m, 100m, null);
        await svc.RegistrarAsync(dto);

        version.Verify(v => v.Invalidar(), Times.Once);
    }

    [Fact]
    public async Task RecalcularStockAsync_Exitoso_InvalidaLaVersionDeReportes()
    {
        var repo    = new Mock<IMovimientoStockRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var version = new Mock<IVersionReportes>();

        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(1, "test-user", RolUsuario.Admin, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var producto = new Producto
        {
            Id = 1, Nombre = "Test", Codigo = "T-001",
            StockActual = 5m, Activo = true, UnidadMedidaId = 1
        };
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.SumarMovimientosAsync(1)).ReturnsAsync((12m, 3));
        repo.Setup(r => r.RecalcularAtomicoAsync(It.IsAny<RecalculoAtomicoArgs>()))
            .Returns(Task.CompletedTask);

        var svc = new MovimientoStockService(repo.Object, session.Object, auth.Object, version.Object);

        await svc.RecalcularStockAsync(1);

        version.Verify(v => v.Invalidar(), Times.Once);
    }
}
