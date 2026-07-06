using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Movimientos;

public class MovimientoStockServiceTests
{
    // ── helpers de setup ────────────────────────────────────────────────────

    private static (MovimientoStockService svc,
                    Mock<IMovimientoStockRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IMovimientoStockRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(idSesion, "test-user", rol, null));

        // Por defecto auth no lanza (permiso concedido)
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new MovimientoStockService(repo.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
    }

    private static Producto ProductoActivo(int id = 1, decimal stock = 20m) =>
        new Producto { Id = id, Nombre = "Test", Codigo = "T-001",
                       StockActual = stock, Activo = true, UnidadMedidaId = 1 };

    private static RegistrarMovimientoDto DtoEntrada(int productoId = 1, decimal cantidad = 5m) =>
        new RegistrarMovimientoDto(productoId, TipoMovimiento.Entrada, MotivoMovimiento.Compra,
                                   cantidad, 100m, null);

    private static RegistrarMovimientoDto DtoSalida(int productoId = 1, decimal cantidad = 5m) =>
        new RegistrarMovimientoDto(productoId, TipoMovimiento.Salida, MotivoMovimiento.Venta,
                                   cantidad, 100m, null);

    // ── B3: Autorización fail-closed ─────────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinPermiso_LanzaExcepcionSinLeerDatos()
    {
        var (svc, repo, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RegistrarMovimientos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.RegistrarAsync(DtoEntrada()));

        // El repo NO debe haber sido invocado
        repo.Verify(r => r.ObtenerProductoAsync(It.IsAny<int>()), Times.Never);
        repo.Verify(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()), Times.Never);
    }

    [Fact]
    public async Task RecalcularStockAsync_SinPermiso_LanzaExcepcionSinLeerDatos()
    {
        var (svc, repo, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RecalcularStock))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.RecalcularStockAsync(1));

        repo.Verify(r => r.ObtenerProductoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_SinPermiso_LanzaExcepcionSinLeerDatos()
    {
        var (svc, repo, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RegistrarMovimientos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerHistorialAsync(new HistorialMovimientoFiltro()));

        repo.Verify(r => r.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Never);
    }

    // ── B4: Validaciones de dominio ──────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task RegistrarAsync_CantidadCeroONegativa_LanzaArgumentException(decimal cantidad)
    {
        var (svc, repo, _, _) = Crear();
        var dto = new RegistrarMovimientoDto(1, TipoMovimiento.Entrada, MotivoMovimiento.Compra,
                                             cantidad, 100m, null);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
        repo.Verify(r => r.ObtenerProductoAsync(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Venta)]   // Entrada+Venta → inválido
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Merma)]   // Entrada+Merma → inválido
    [InlineData(TipoMovimiento.Salida,  MotivoMovimiento.Compra)]  // Salida+Compra → inválido
    public async Task RegistrarAsync_TipoMotivoIncompatible_LanzaArgumentException(
        TipoMovimiento tipo, MotivoMovimiento motivo)
    {
        var (svc, repo, _, _) = Crear();
        var dto = new RegistrarMovimientoDto(1, tipo, motivo, 5m, null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
        repo.Verify(r => r.ObtenerProductoAsync(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Compra)]  // Compra requiere precio
    [InlineData(TipoMovimiento.Salida,  MotivoMovimiento.Venta)]   // Venta requiere precio
    public async Task RegistrarAsync_CompraVentaSinPrecio_LanzaArgumentException(
        TipoMovimiento tipo, MotivoMovimiento motivo)
    {
        var (svc, repo, _, _) = Crear();
        var dto = new RegistrarMovimientoDto(1, tipo, motivo, 5m, null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
        repo.Verify(r => r.ObtenerProductoAsync(It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(TipoMovimiento.Entrada, MotivoMovimiento.Ajuste)]
    [InlineData(TipoMovimiento.Salida,  MotivoMovimiento.Ajuste)]
    [InlineData(TipoMovimiento.Salida,  MotivoMovimiento.Merma)]
    public async Task RegistrarAsync_AjusteMermaSinPrecio_NoPasaValidacionPrecioYSigueAlProducto(
        TipoMovimiento tipo, MotivoMovimiento motivo)
    {
        var (svc, repo, _, _) = Crear();
        var dto = new RegistrarMovimientoDto(1, tipo, motivo, 5m, null, null);

        // El repo devuelve null → lanzará KeyNotFoundException más adelante,
        // pero lo importante es que la validación de precio NO lo bloqueó.
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RegistrarAsync(dto));
        repo.Verify(r => r.ObtenerProductoAsync(1), Times.Once); // llegó al repo
    }

    // ── B5: Existencia y estado del producto ─────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_ProductoNoExiste_LanzaKeyNotFoundException()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(99)).ReturnsAsync((Producto?)null);

        var dto = DtoEntrada(productoId: 99);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInactivo_LanzaInvalidOperationException()
    {
        var (svc, repo, _, _) = Crear();
        var producto = new Producto { Id = 1, Activo = false, Nombre = "X",
                                      Codigo = "X", StockActual = 10m, UnidadMedidaId = 1 };
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);

        var dto = DtoEntrada();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RegistrarAsync(dto));
    }

    // ── B6: Cálculo de signo + StockInsuficienteException ───────────────────

    [Fact]
    public async Task RegistrarAsync_SalidaStockInsuficiente_LanzaStockInsuficienteException()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 3m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);

        var dto = DtoSalida(cantidad: 10m); // 3 - 10 = -7 → insuficiente

        await Assert.ThrowsAsync<StockInsuficienteException>(() => svc.RegistrarAsync(dto));
        repo.Verify(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_SalidaForzar_PermiteStockNegativoYLlamaRepo()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 3m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(42);

        var dto = DtoSalida(cantidad: 10m);
        var result = await svc.RegistrarAsync(dto, forzar: true);

        // Stock negativo es aceptable con forzar=true
        Assert.Equal(-7m, result.StockNuevo);
        repo.Verify(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()), Times.Once);
    }

    [Fact]
    public async Task RegistrarAsync_Entrada_SumaAlStock()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 10m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(7);

        var dto = DtoEntrada(cantidad: 5m);
        var result = await svc.RegistrarAsync(dto);

        Assert.Equal(15m, result.StockNuevo);
        Assert.Equal(10m, result.StockAnterior);
    }

    // ── B7: Registro exitoso + retorno DTO ───────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_EntradaExitosa_RetornaMovimientoRegistradoDto()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 5m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(99);

        var dto = new RegistrarMovimientoDto(1, TipoMovimiento.Entrada, MotivoMovimiento.Compra,
                                             10m, 150m, "lote A");
        var result = await svc.RegistrarAsync(dto);

        Assert.Equal(99, result.MovimientoId);
        Assert.Equal(1,  result.ProductoId);
        Assert.Equal(TipoMovimiento.Entrada, result.Tipo);
        Assert.Equal(MotivoMovimiento.Compra, result.Motivo);
        Assert.Equal(10m,  result.Cantidad);
        Assert.Equal(150m, result.PrecioUnitario);
        Assert.Equal(5m,   result.StockAnterior);
        Assert.Equal(15m,  result.StockNuevo);
        repo.Verify(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()), Times.Once);
    }

    [Fact]
    public async Task RegistrarAsync_SalidaExitosa_RetornaMovimientoRegistradoDto()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 20m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(100);

        var dto = DtoSalida(cantidad: 8m);
        var result = await svc.RegistrarAsync(dto);

        Assert.Equal(100, result.MovimientoId);
        Assert.Equal(20m, result.StockAnterior);
        Assert.Equal(12m, result.StockNuevo);
    }

    [Fact]
    public async Task RegistrarAsync_LlamaAtomicoExactamenteUnaVez()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(ProductoActivo());
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(1);

        await svc.RegistrarAsync(DtoEntrada());

        repo.Verify(r => r.RegistrarMovimientoAtomicoAsync(
            It.IsAny<RegistroAtomicoArgs>()), Times.Once);
    }

    // ── B8: RecalcularStockAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RecalcularStockAsync_ProductoExiste_ActualizaStockYLlamaRecalcularAtomico()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 5m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.SumarMovimientosAsync(1)).ReturnsAsync((12m, 3));
        repo.Setup(r => r.RecalcularAtomicoAsync(It.IsAny<RecalculoAtomicoArgs>()))
            .Returns(Task.CompletedTask);

        var result = await svc.RecalcularStockAsync(1);

        Assert.Equal(1,   result.ProductoId);
        Assert.Equal(5m,  result.StockAnterior);
        Assert.Equal(12m, result.StockNuevo);
        Assert.Equal(3,   result.TotalMovimientos);
        repo.Verify(r => r.RecalcularAtomicoAsync(
            It.Is<RecalculoAtomicoArgs>(a => a.ProductoId == 1 && a.StockNuevo == 12m)),
            Times.Once);
    }

    [Fact]
    public async Task RecalcularStockAsync_SinMovimientos_SetearStockCero()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 8m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.SumarMovimientosAsync(1)).ReturnsAsync((0m, 0));
        repo.Setup(r => r.RecalcularAtomicoAsync(It.IsAny<RecalculoAtomicoArgs>()))
            .Returns(Task.CompletedTask);

        var result = await svc.RecalcularStockAsync(1);

        Assert.Equal(0m, result.StockNuevo);
        Assert.Equal(0,  result.TotalMovimientos);
    }

    [Fact]
    public async Task RecalcularStockAsync_ProductoNoExiste_LanzaKeyNotFoundException()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerProductoAsync(99)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => svc.RecalcularStockAsync(99));
    }

    [Fact]
    public async Task RecalcularStockAsync_Idempotente_MismoResultadoEnDosLlamadas()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 5m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.SumarMovimientosAsync(1)).ReturnsAsync((10m, 2));
        repo.Setup(r => r.RecalcularAtomicoAsync(It.IsAny<RecalculoAtomicoArgs>()))
            .Returns(Task.CompletedTask);

        var r1 = await svc.RecalcularStockAsync(1);
        var r2 = await svc.RecalcularStockAsync(1);

        Assert.Equal(r1.StockNuevo, r2.StockNuevo);
        Assert.Equal(r1.TotalMovimientos, r2.TotalMovimientos);
    }

    // ── B8: ObtenerHistorialAsync ─────────────────────────────────────────────

    [Fact]
    public async Task ObtenerHistorialAsync_SinFiltros_DelegaAlRepoYDevuelveResultado()
    {
        var (svc, repo, _, _) = Crear();
        var esperado = new List<MovimientoHistorialDto>
        {
            new(1, 1, "P", TipoMovimiento.Entrada, MotivoMovimiento.Compra,
                10m, 100m, 0m, 10m, null, DateTime.UtcNow, 1, "Admin")
        };
        repo.Setup(r => r.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(esperado);

        var result = await svc.ObtenerHistorialAsync(new HistorialMovimientoFiltro());

        Assert.Single(result);
        repo.Verify(r => r.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_SinResultados_DevuelveListaVacia()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(new List<MovimientoHistorialDto>());

        var result = await svc.ObtenerHistorialAsync(new HistorialMovimientoFiltro());

        Assert.Empty(result);
    }
}
