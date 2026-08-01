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

public class IngresoPorFacturaServiceTests
{
    private static (IngresoPorFacturaService svc,
                    Mock<IMovimientoStockRepository> movRepo,
                    Mock<IGastoRepository> gastoRepo,
                    Mock<IProveedorRepository> proveedores,
                    Mock<IFuenteFinanciamientoRepository> fuentes,
                    Mock<IRubroGastoRepository> rubros,
                    Mock<ILineaPoaRepository> lineasPoa,
                    Mock<IProductoRepository> productos,
                    Mock<IUnidadMedidaRepository> unidades,
                    Mock<ICurrentSession> session,
                    Mock<IAuthSvc> auth)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var movRepo    = new Mock<IMovimientoStockRepository>();
        var gastoRepo  = new Mock<IGastoRepository>();
        var proveedores = new Mock<IProveedorRepository>();
        var fuentes    = new Mock<IFuenteFinanciamientoRepository>();
        var rubros     = new Mock<IRubroGastoRepository>();
        var lineasPoa  = new Mock<ILineaPoaRepository>();
        var productos  = new Mock<IProductoRepository>();
        var unidades   = new Mock<IUnidadMedidaRepository>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(idSesion, "test-user", rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        proveedores.Setup(p => p.ObtenerPorIdAsync(1))
            .ReturnsAsync(new Proveedor { Id = 1, Nombre = "Proveedor Test", Activo = true });
        fuentes.Setup(f => f.ObtenerPorIdAsync(1))
            .ReturnsAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Fuente Test", Activo = true });
        rubros.Setup(r => r.ObtenerPorIdAsync(1))
            .ReturnsAsync(new RubroGasto { Id = 1, Codigo = 1, Nombre = "Rubro Test", Activo = true });
        unidades.Setup(u => u.ObtenerPorIdAsync(1))
            .ReturnsAsync(new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true });
        productos.Setup(p => p.ExisteCodigoAsync(It.IsAny<string>(), null)).ReturnsAsync(false);

        var svc = new IngresoPorFacturaService(
            movRepo.Object, gastoRepo.Object, proveedores.Object, fuentes.Object, rubros.Object,
            lineasPoa.Object, productos.Object, unidades.Object, session.Object, auth.Object,
            Mock.Of<StockApp.Application.Reportes.IVersionReportes>());

        return (svc, movRepo, gastoRepo, proveedores, fuentes, rubros, lineasPoa, productos, unidades, session, auth);
    }

    private static RenglonFacturaDto RenglonExistente(
        int productoId = 1, decimal cantidad = 5m, decimal precio = 100m, bool actualizarPrecio = false) =>
        new(productoId, null, cantidad, precio, actualizarPrecio);

    private static IngresoPorFacturaDto DtoValido(params RenglonFacturaDto[] renglones) => new(
        ProveedorId: 1, NumeroFactura: "A-0001", NumeroOrden: null,
        Fecha: DateTime.UtcNow, Detalle: "Compra de insumos", Destino: null,
        MontoTotal: 500m, FuenteFinanciamientoId: 1, RubroGastoId: 1, LineaPoaId: null,
        CondicionPago: CondicionPago.Contado, FechaVencimiento: null,
        Renglones: renglones.Length == 0 ? new[] { RenglonExistente() } : renglones);

    // ── Autorización fail-closed ──────────────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinPermisoMovimientos_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RegistrarMovimientos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido()));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_ConProductoNuevo_SinPermisoCatalogo_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarProductos))
            .Throws<UnauthorizedAccessException>();

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N1", "Producto nuevo", null, 1, 50m), 3m, 20m, false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido(renglon)));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_SinPermisoGastos_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RegistrarGastos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido()));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    // ── Validaciones de renglones y cabecera ─────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinRenglones_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido() with { Renglones = Array.Empty<RenglonFacturaDto>() };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RegistrarAsync_CantidadCeroONegativa_LanzaArgumentException(decimal cantidad)
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido(RenglonExistente(cantidad: cantidad));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_PrecioUnitarioNegativo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido(RenglonExistente(precio: -1m));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_MontoTotalCeroONegativo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido() with { MontoTotal = 0m };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_RenglonSinProductoIdNiProductoNuevo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var renglon = new RenglonFacturaDto(null, null, 1m, 10m, false);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(DtoValido(renglon)));
    }

    [Fact]
    public async Task RegistrarAsync_RenglonConProductoIdYProductoNuevoALaVez_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _) = Crear();
        var renglon = new RenglonFacturaDto(
            1, new ProductoNuevoDto("X", "Y", null, 1, 1m), 1m, 10m, false);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(DtoValido(renglon)));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoNuevoConUnidadMedidaInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, movRepo, _, _, _, _, _, _, unidades, _, _) = Crear();
        unidades.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync((UnidadMedida?)null);

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N1", "Producto nuevo", null, 99, 50m), 3m, 20m, false);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(DtoValido(renglon)));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInactivo_LanzaReglaDeNegocio()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = false, StockActual = 10m });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoDuplicadoEnDosRenglones_SeAcepta()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = true, StockActual = 10m, PrecioCosto = 5m });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(1, new List<int> { 10, 11 }));

        var dto = DtoValido(RenglonExistente(cantidad: 2m), RenglonExistente(cantidad: 3m));

        var resultado = await svc.RegistrarAsync(dto);

        Assert.Equal(1, resultado.GastoId);
        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(
            It.Is<IngresoPorFacturaArgs>(a => a.Renglones.Count == 2)), Times.Once);
    }

    // ── Camino feliz: totales y delegación ────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_DatosValidos_DelegaAlRepoYCalculaTotales()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = true, StockActual = 10m, PrecioCosto = 5m });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(7, new List<int> { 100 }));

        var dto = DtoValido(RenglonExistente(cantidad: 5m, precio: 90m)) with { MontoTotal = 500m };

        var resultado = await svc.RegistrarAsync(dto);

        Assert.Equal(7, resultado.GastoId);
        Assert.Equal(450m, resultado.SumaRenglones);       // 5 * 90
        Assert.Equal(50m, resultado.DiferenciaConTotal);    // 500 - 450
    }

    [Fact]
    public async Task RegistrarAsync_ProductoNuevo_ConPermisoCatalogo_LlegaAlRepoConStockYPrecioCorrectos()
    {
        // El caso "sin permiso" ya está cubierto por
        // RegistrarAsync_ConProductoNuevo_SinPermisoCatalogo_LanzaExcepcionSinTocarElRepo (Task 1).
        // Este test confirma que CON permiso (rol Admin por defecto de Crear()), un renglón de
        // producto nuevo SÍ llega al repo con ProductoNuevo seteado, StockActual == Cantidad del
        // renglón y PrecioCosto == PrecioUnitario del renglón.
        var (svc, movRepo, _, _, _, _, _, _, unidades, _, _) = Crear();
        unidades.Setup(u => u.ObtenerPorIdAsync(2))
            .ReturnsAsync(new UnidadMedida { Id = 2, Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(1, new List<int> { 1 }));

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N2", "Producto Nuevo", null, 2, 80m), 4m, 35m, false);

        await svc.RegistrarAsync(DtoValido(renglon));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.Is<IngresoPorFacturaArgs>(a =>
            a.Renglones.Single().ProductoNuevo!.Codigo == "SKU-N2"
            && a.Renglones.Single().ProductoNuevo!.PrecioCosto == 35m
            && a.Renglones.Single().ProductoNuevo!.StockActual == 4m)), Times.Once);
    }
}
