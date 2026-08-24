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
                    Mock<IAuthSvc> auth,
                    Mock<IAuditLogger> audit)
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
        var audit      = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(
            new StockApp.Application.Auth.UsuarioSesion(idSesion, "test-user", rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

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
            Mock.Of<StockApp.Application.Reportes.IVersionReportes>(), audit.Object);

        return (svc, movRepo, gastoRepo, proveedores, fuentes, rubros, lineasPoa, productos, unidades, session, auth, audit);
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
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth, _) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.RegistrarMovimientos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido()));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_ConProductoNuevo_SinPermisoCatalogo_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth, _) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarProductos))
            .Throws<UnauthorizedAccessException>();

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N1", "Producto nuevo", null, 1), 3m, 20m, false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido(renglon)));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_SinPermisoGastos_LanzaExcepcionSinTocarElRepo()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, auth, _) = Crear();
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.RegistrarGastos))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.RegistrarAsync(DtoValido()));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    // ── Validaciones de renglones y cabecera ─────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_SinRenglones_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido() with { Renglones = Array.Empty<RenglonFacturaDto>() };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task RegistrarAsync_CantidadCeroONegativa_LanzaArgumentException(decimal cantidad)
    {
        var (svc, _, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido(RenglonExistente(cantidad: cantidad));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_PrecioUnitarioNegativo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido(RenglonExistente(precio: -1m));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_MontoTotalCeroONegativo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _, _) = Crear();
        var dto = DtoValido() with { MontoTotal = 0m };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(dto));
    }

    [Fact]
    public async Task RegistrarAsync_RenglonSinProductoIdNiProductoNuevo_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _, _) = Crear();
        var renglon = new RenglonFacturaDto(null, null, 1m, 10m, false);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(DtoValido(renglon)));
    }

    [Fact]
    public async Task RegistrarAsync_RenglonConProductoIdYProductoNuevoALaVez_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _, _, _, _, _, _) = Crear();
        var renglon = new RenglonFacturaDto(
            1, new ProductoNuevoDto("X", "Y", null, 1), 1m, 10m, false);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.RegistrarAsync(DtoValido(renglon)));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoNuevoConUnidadMedidaInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, movRepo, _, _, _, _, _, _, unidades, _, _, _) = Crear();
        unidades.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync((UnidadMedida?)null);

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N1", "Producto nuevo", null, 99), 3m, 20m, false);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(DtoValido(renglon)));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync((Producto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoInactivo_LanzaReglaDeNegocio()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = false, StockActual = 10m });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task RegistrarAsync_ProductoDuplicadoEnDosRenglones_SeAcepta()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _, _) = Crear();
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
        var (svc, movRepo, _, _, _, _, _, _, _, _, _, _) = Crear();
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
        var (svc, movRepo, _, _, _, _, _, _, unidades, _, _, _) = Crear();
        unidades.Setup(u => u.ObtenerPorIdAsync(2))
            .ReturnsAsync(new UnidadMedida { Id = 2, Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .ReturnsAsync(new ResultadoIngresoPorFactura(1, new List<int> { 1 }));

        var renglon = new RenglonFacturaDto(
            null, new ProductoNuevoDto("SKU-N2", "Producto Nuevo", null, 2), 4m, 35m, false);

        await svc.RegistrarAsync(DtoValido(renglon));

        movRepo.Verify(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.Is<IngresoPorFacturaArgs>(a =>
            a.Renglones.Single().ProductoNuevo!.Codigo == "SKU-N2"
            && a.Renglones.Single().ProductoNuevo!.PrecioCosto == 35m
            && a.Renglones.Single().ProductoNuevo!.StockActual == 4m)), Times.Once);
    }

    // ── Pago automático de contado (unifica la regla ya existente en GastoService.AltaAsync) ──

    [Fact]
    public async Task RegistrarAsync_Contado_CreaPagoAutomaticoPorElTotalEnLaFechaDelGasto()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = true, StockActual = 10m, PrecioCosto = 5m });
        IngresoPorFacturaArgs? argsCapturados = null;
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .Callback<IngresoPorFacturaArgs>(a => argsCapturados = a)
            .ReturnsAsync(new ResultadoIngresoPorFactura(7, new List<int> { 100 }));

        var dto = DtoValido() with { MontoTotal = 500m };

        await svc.RegistrarAsync(dto);

        var pago = Assert.Single(argsCapturados!.Gasto.Pagos);
        Assert.Equal(500m, pago.Monto);
        Assert.Equal(dto.Fecha, pago.Fecha);
        Assert.True(pago.EsAutomatico);
    }

    [Fact]
    public async Task RegistrarAsync_Credito_NoCreaPagoAutomatico()
    {
        var (svc, movRepo, _, _, _, _, _, _, _, _, _, _) = Crear();
        movRepo.Setup(r => r.ObtenerProductoAsync(1))
            .ReturnsAsync(new Producto { Id = 1, Codigo = "SKU1", Activo = true, StockActual = 10m, PrecioCosto = 5m });
        IngresoPorFacturaArgs? argsCapturados = null;
        movRepo.Setup(r => r.RegistrarIngresoPorFacturaAtomicoAsync(It.IsAny<IngresoPorFacturaArgs>()))
            .Callback<IngresoPorFacturaArgs>(a => argsCapturados = a)
            .ReturnsAsync(new ResultadoIngresoPorFactura(7, new List<int> { 100 }));

        var dto = DtoValido() with
        {
            MontoTotal = 500m, CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(30),
        };

        await svc.RegistrarAsync(dto);

        Assert.Empty(argsCapturados!.Gasto.Pagos);
    }

    // ── AnularLoteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnularLoteAsync_GastoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, _, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(99)).ReturnsAsync((Gasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.AnularLoteAsync(99));
    }

    [Fact]
    public async Task AnularLoteAsync_GastoYaAnulado_LanzaReglaDeNegocio()
    {
        var (svc, _, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(5)).ReturnsAsync(new Gasto { Id = 5, Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(5));
    }

    [Fact]
    public async Task AnularLoteAsync_GastoConPagosActivos_LanzaReglaDeNegocio()
    {
        var (svc, _, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(6)).ReturnsAsync(new Gasto
        {
            Id = 6, Activo = true,
            Pagos = new List<PagoGasto> { new() { Id = 1, Monto = 100m, Activo = true } },
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(6));
    }

    [Fact]
    public async Task AnularLoteAsync_GastoYaAnuladoDetectadoBajoLockEnElRepo_LanzaReglaDeNegocio()
    {
        // Ronda de correcciones 1, Hallazgo 1: simula la carrera que motivó el fix — el chequeo
        // optimista de arriba (gastoRepo.ObtenerPorIdAsync) ve el gasto todavía Activo=true
        // (lectura stale, otra llamada concurrente ya lo anuló en la BD), pero la re-verificación
        // bajo lock DENTRO de la transacción del repo detecta el estado real y devuelve
        // GastoYaAnulado. El service debe traducir eso a ReglaDeNegocioException igual que si el
        // chequeo optimista lo hubiera detectado.
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(9)).ReturnsAsync(new Gasto { Id = 9, Activo = true });
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(9)).ReturnsAsync(true);
        movRepo.Setup(m => m.AnularIngresoPorFacturaAtomicoAsync(9, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.GastoYaAnulado, Array.Empty<ItemFaltanteStock>()));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(9));
    }

    [Fact]
    public async Task AnularLoteAsync_StockInsuficiente_LanzaReglaDeNegocioConElDetalle()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(7)).ReturnsAsync(new Gasto { Id = 7, Activo = true });
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(7)).ReturnsAsync(true);
        movRepo.Setup(m => m.AnularIngresoPorFacturaAtomicoAsync(7, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(
                ResultadoAnulacionIngresoEstado.StockInsuficiente,
                new List<ItemFaltanteStock> { new(1, "Producto X", 2m, 5m) }));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(7));
        Assert.Contains("Producto X", ex.Message);
    }

    [Fact]
    public async Task AnularLoteAsync_DatosValidos_DelegaAlRepo()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(8)).ReturnsAsync(new Gasto { Id = 8, Activo = true });
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(8)).ReturnsAsync(true);
        movRepo.Setup(m => m.AnularIngresoPorFacturaAtomicoAsync(8, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.Ok, Array.Empty<ItemFaltanteStock>()));

        await svc.AnularLoteAsync(8);

        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(8, It.IsAny<int>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnularLoteAsync_GastoSinMovimientosAsociados_LanzaReglaDeNegocioSinTocarElRepo()
    {
        // Fix 4 (revisión final): el spec exige "tenga movimientos asociados" como cuarta
        // condición de AnularLoteAsync — anular por esta ruta un gasto de servicios (sin
        // movimientos de stock) no debe auditarse como AnulacionIngresoPorFactura.
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(10)).ReturnsAsync(new Gasto { Id = 10, Activo = true });
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(10)).ReturnsAsync(false);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AnularLoteAsync(10));

        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    // ── Anulación en cascada del pago automático de contado ──────────────────

    [Fact]
    public async Task AnularLoteAsync_ConPagoAutomaticoSinConfirmar_LanzaExcepcionEstructurada()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, audit) = Crear();
        var gasto = new Gasto { Id = 11, Activo = true };
        var pago20 = PagoGasto.Automatico(DateTime.UtcNow, 500m, "Pago contado (automático)");
        pago20.Id = 20;
        pago20.GastoId = 11;
        gasto.Pagos.Add(pago20);
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(11)).ReturnsAsync(gasto);
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(11)).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<AnulacionRequierePagoAutomaticoConfirmadoException>(
            () => svc.AnularLoteAsync(11));

        Assert.Equal(11, ex.GastoId);
        Assert.Equal(500m, ex.MontoPagoAutomatico);
        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        gastoRepo.Verify(g => g.ActualizarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularLoteAsync_ConPagoAutomaticoConfirmado_AnulaPagoRevierteStockUnaSolaVezYAudita()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, audit) = Crear();
        var gasto = new Gasto { Id = 12, Activo = true };
        var pago21 = PagoGasto.Automatico(DateTime.UtcNow, 500m, "Pago contado (automático)");
        pago21.Id = 21;
        pago21.GastoId = 12;
        gasto.Pagos.Add(pago21);
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(12)).ReturnsAsync(gasto);
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(12)).ReturnsAsync(true);
        movRepo.Setup(m => m.AnularIngresoPorFacturaAtomicoAsync(12, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync(new ResultadoAnulacionIngreso(ResultadoAnulacionIngresoEstado.Ok, Array.Empty<ItemFaltanteStock>()));

        await svc.AnularLoteAsync(12, confirmarAnulacionDePagoAutomatico: true);

        gastoRepo.Verify(g => g.ActualizarPagoAsync(
            It.Is<PagoGasto>(p => p.Id == 21 && !p.Activo)), Times.Once);
        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(12, It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AnulacionPagoGasto, "PagoGasto", 21, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnularLoteAsync_ConPagoManualActivoConfirmando_SigueLanzandoReglaDeNegocioSinTocarNada()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        var gasto = new Gasto { Id = 13, Activo = true };
        gasto.Pagos.Add(new PagoGasto { Id = 22, GastoId = 13, Monto = 200m, Activo = true });
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(13)).ReturnsAsync(gasto);
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(13)).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AnularLoteAsync(13, confirmarAnulacionDePagoAutomatico: true));

        Assert.IsType<ReglaDeNegocioException>(ex);
        gastoRepo.Verify(g => g.ActualizarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AnularLoteAsync_ConPagoAutomaticoYManualActivosConfirmando_LanzaReglaDeNegocioSinAnularNada()
    {
        var (svc, movRepo, gastoRepo, _, _, _, _, _, _, _, _, _) = Crear();
        var gasto = new Gasto { Id = 14, Activo = true };
        var pago23 = PagoGasto.Automatico(DateTime.UtcNow, 500m, "Pago contado (automático)");
        pago23.Id = 23;
        pago23.GastoId = 14;
        gasto.Pagos.Add(pago23);
        gasto.Pagos.Add(new PagoGasto { Id = 24, GastoId = 14, Monto = 200m, Activo = true });
        gastoRepo.Setup(g => g.ObtenerPorIdAsync(14)).ReturnsAsync(gasto);
        movRepo.Setup(m => m.ExistenMovimientosDeGastoAsync(14)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AnularLoteAsync(14, confirmarAnulacionDePagoAutomatico: true));

        gastoRepo.Verify(g => g.ActualizarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
        movRepo.Verify(m => m.AnularIngresoPorFacturaAtomicoAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }
}
