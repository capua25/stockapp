using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class GastoServiceTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Mocks(
        GastoService Svc,
        Mock<IGastoRepository> Repo,
        Mock<IProveedorRepository> Proveedores,
        Mock<IFuenteFinanciamientoRepository> Fuentes,
        Mock<IRubroGastoRepository> Rubros,
        Mock<ILineaPoaRepository> LineasPoa,
        Mock<IAuditLogger> Audit);

    private static Mocks Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo       = new Mock<IGastoRepository>();
        var proveedores = new Mock<IProveedorRepository>();
        var fuentes    = new Mock<IFuenteFinanciamientoRepository>();
        var rubros     = new Mock<IRubroGastoRepository>();
        var lineasPoa  = new Mock<ILineaPoaRepository>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();
        var audit      = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        // Maestros por defecto: existen y están activos (los tests puntuales los pisan)
        proveedores.Setup(p => p.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new Proveedor { Id = id, Nombre = $"Proveedor {id}", Activo = true });
        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });
        rubros.Setup(r => r.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new RubroGasto { Id = id, Codigo = id, Nombre = $"Rubro {id}", Activo = true });

        var svc = new GastoService(
            repo.Object, proveedores.Object, fuentes.Object, rubros.Object, lineasPoa.Object,
            session.Object, auth.Object, audit.Object);
        return new Mocks(svc, repo, proveedores, fuentes, rubros, lineasPoa, audit);
    }

    private static Gasto GastoValido(CondicionPago condicion = CondicionPago.Credito) => new()
    {
        ProveedorId = 1,
        NumeroFactura = "A-0001",
        Detalle = "Materiales de obra",
        Fecha = Hoy,
        MontoTotal = 1000m,
        FuenteFinanciamientoId = 2,
        RubroGastoId = 3,
        CondicionPago = condicion,
        FechaVencimiento = condicion == CondicionPago.Credito ? Hoy.AddDays(30) : null,
    };

    // ── Alta ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_DetalleVacio_LanzaArgumentException()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Detalle = "  ";

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_MontoNoPositivo_LanzaArgumentException()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.MontoTotal = 0m;

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_CreditoSinVencimiento_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.FechaVencimiento = null;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_ContadoConVencimiento_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido(CondicionPago.Contado);
        gasto.FechaVencimiento = Hoy.AddDays(10);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_Contado_CreaPagoAutomaticoPorElTotal()
    {
        var m = Crear();
        Gasto? persistido = null;
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>()))
            .Callback<Gasto>(g => persistido = g)
            .ReturnsAsync(7);

        var resultado = await m.Svc.AltaAsync(GastoValido(CondicionPago.Contado));

        Assert.Equal(7, resultado.Id);
        Assert.Null(resultado.AdvertenciaSobregiro);
        var pago = Assert.Single(persistido!.Pagos);
        Assert.Equal(1000m, pago.Monto);
        Assert.Equal(Hoy, pago.Fecha);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaGasto, "Gasto", 7, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AltaAsync_Credito_NoCreaPagoAutomatico()
    {
        var m = Crear();
        Gasto? persistido = null;
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>()))
            .Callback<Gasto>(g => persistido = g)
            .ReturnsAsync(8);

        await m.Svc.AltaAsync(GastoValido());

        Assert.Empty(persistido!.Pagos);
    }

    [Fact]
    public async Task AltaAsync_FacturaDuplicadaDeProveedorActiva_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerPorProveedorYFacturaAsync(1, "A-0001", null))
            .ReturnsAsync(new Gasto { Id = 99, ProveedorId = 1, NumeroFactura = "A-0001" });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(GastoValido()));
    }

    [Fact]
    public async Task AltaAsync_MismaFacturaDistintoNumeroOrden_NoLanza()
    {
        // F5c: el chequeo de unicidad en memoria (ValidarFacturaUnicaAsync) tiene que espejar el
        // índice ampliado (Proveedor, Factura, Orden) — pasa el NumeroOrden del gasto al repo, no
        // solo Proveedor+Factura. El repo mockeado solo "encuentra" algo para un orden distinto
        // ("OTRO-ORDEN"), así que para el orden real del gasto ("MI-ORDEN") debe dar null y el
        // alta debe pasar.
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerPorProveedorYFacturaAsync(1, "A-0001", "OTRO-ORDEN"))
            .ReturnsAsync(new Gasto { Id = 99, ProveedorId = 1, NumeroFactura = "A-0001", NumeroOrden = "OTRO-ORDEN" });
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>())).ReturnsAsync(12);
        var gasto = GastoValido();
        gasto.NumeroOrden = "MI-ORDEN";

        var resultado = await m.Svc.AltaAsync(gasto);

        Assert.Equal(12, resultado.Id);
        m.Repo.Verify(r => r.ObtenerPorProveedorYFacturaAsync(1, "A-0001", "MI-ORDEN"), Times.Once);
    }

    [Fact]
    public async Task AltaAsync_FuenteInactiva_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.Fuentes.Setup(f => f.ObtenerPorIdAsync(2))
            .ReturnsAsync(new FuenteFinanciamiento { Id = 2, Nombre = "Vieja", Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(GastoValido()));
    }

    [Fact]
    public async Task AltaAsync_LineaPoaSinAsignacionParaLaFuente_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.LineasPoa.Setup(l => l.ObtenerPorIdAsync(5)).ReturnsAsync(new LineaPoa
        {
            Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 99, Monto = 1000m } },
        });
        var gasto = GastoValido();
        gasto.LineaPoaId = 5;  // fuente 2 no tiene asignación en la línea

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_SobregiroDeLinea_AdvierteYNoBloquea()
    {
        var m = Crear();
        m.LineasPoa.Setup(l => l.ObtenerPorIdAsync(5)).ReturnsAsync(new LineaPoa
        {
            Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 5000m } },
        });
        m.Repo.Setup(r => r.TotalGastadoLineaFuenteAsync(5, 2, null)).ReturnsAsync(4500m);
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>())).ReturnsAsync(10);
        var gasto = GastoValido();          // 1000: 4500 + 1000 > 5000 ⇒ sobregiro 500
        gasto.LineaPoaId = 5;

        var resultado = await m.Svc.AltaAsync(gasto);

        Assert.Equal(10, resultado.Id);     // se registró IGUAL (spec §10: advierte, no bloquea)
        Assert.NotNull(resultado.AdvertenciaSobregiro);
        Assert.Contains("PRENSA", resultado.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task AltaAsync_ConMovimientos_ValidaYAsigna()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 40, Tipo = TipoMovimiento.Entrada, GastoId = null },
            });
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>())).ReturnsAsync(11);

        await m.Svc.AltaAsync(GastoValido(), new[] { 40 });

        m.Repo.Verify(r => r.AsignarGastoAMovimientosAsync(11,
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
    }

    [Fact]
    public async Task AltaAsync_MovimientoDeSalida_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 41, Tipo = TipoMovimiento.Salida, GastoId = null },
            });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => m.Svc.AltaAsync(GastoValido(), new[] { 41 }));
    }

    // ── Modificación ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GastoAnulado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        original.Activo = false;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.ModificarAsync(editado));
    }

    [Fact]
    public async Task ModificarAsync_CambiaCondicionDePago_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido(CondicionPago.Contado);
        editado.Id = 1;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.ModificarAsync(editado));
    }

    [Fact]
    public async Task ModificarAsync_MontoMenorALoPagado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        original.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 800m });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;
        editado.MontoTotal = 500m;   // < 800 pagado

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.ModificarAsync(editado));
    }

    [Fact]
    public async Task ModificarAsync_CambiaDetalleYMonto_ActualizaYAudita()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;
        editado.Detalle = "Materiales de obra (ampliación)";
        editado.MontoTotal = 1500m;

        var resultado = await m.Svc.ModificarAsync(editado);

        Assert.Equal(1, resultado.Id);
        m.Repo.Verify(r => r.ActualizarAsync(It.Is<Gasto>(g =>
            g.Detalle == "Materiales de obra (ampliación)" && g.MontoTotal == 1500m)), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionGasto, "Gasto", 1,
            It.Is<string>(d => d.Contains("Detalle") && d.Contains("Monto"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;

        await m.Svc.ModificarAsync(editado);

        m.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Gasto>()), Times.Never);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    // ── Pagos ────────────────────────────────────────────────────────────────

    // La re-verificación de saldo/anulado ahora vive DENTRO de la transacción atómica del
    // repo (GastoRepository.RegistrarPagoAtomicoAsync, FOR UPDATE) — cierra la ventana de
    // sobrepago concurrente que el check-then-insert en memoria dejaba abierta. Estos tests
    // unitarios verifican que el service delega y propaga; la regla de negocio en sí (saldo,
    // anulado, carrera real) se cubre con Postgres real en
    // GastoRepositoryConcurrenciaTests.DosPagosSimultaneos_SuperanElSaldo_UnoSoloTieneExito.

    [Fact]
    public async Task RegistrarPagoAsync_RepoRechazaPorSaldo_PropagaReglaDeNegocio()
    {
        var m = Crear();
        m.Repo.Setup(r => r.RegistrarPagoAtomicoAsync(It.IsAny<PagoGasto>()))
            .ThrowsAsync(new ReglaDeNegocioException("El pago (300) supera el saldo pendiente de la factura (200)."));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 300m }));
    }

    [Fact]
    public async Task RegistrarPagoAsync_RepoRechazaPorAnulado_PropagaReglaDeNegocio()
    {
        var m = Crear();
        m.Repo.Setup(r => r.RegistrarPagoAtomicoAsync(It.IsAny<PagoGasto>()))
            .ThrowsAsync(new ReglaDeNegocioException("No se pueden registrar pagos sobre un gasto anulado."));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 100m }));
    }

    [Fact]
    public async Task RegistrarPagoAsync_Valido_PersisteYAudita()
    {
        var m = Crear();
        m.Repo.Setup(r => r.RegistrarPagoAtomicoAsync(It.IsAny<PagoGasto>())).ReturnsAsync(21);

        var pagoId = await m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 1000m, Nota = "pago total" });

        Assert.Equal(21, pagoId);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaPagoGasto, "PagoGasto", 21,
            It.Is<string>(d => d.Contains("1000"))), Times.Once);
    }

    [Fact]
    public async Task RegistrarPagoAsync_MontoInvalido_NoLlamaAlRepo()
    {
        var m = Crear();

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 0m }));

        m.Repo.Verify(r => r.RegistrarPagoAtomicoAsync(It.IsAny<PagoGasto>()), Times.Never);
    }

    [Fact]
    public async Task AnularPagoAsync_PagoActivo_AnulaYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        var pago = new PagoGasto { Id = 21, GastoId = 1, Fecha = Hoy, Monto = 500m, Activo = true };
        gasto.Pagos.Add(pago);
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await m.Svc.AnularPagoAsync(1, 21);

        m.Repo.Verify(r => r.ActualizarPagoAsync(It.Is<PagoGasto>(p => p.Id == 21 && !p.Activo)), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AnulacionPagoGasto, "PagoGasto", 21, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnularPagoAsync_PagoYaAnulado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { Id = 21, GastoId = 1, Fecha = Hoy, Monto = 500m, Activo = false });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AnularPagoAsync(1, 21));
    }

    // ── Anulación del gasto ──────────────────────────────────────────────────

    [Fact]
    public async Task AnularAsync_ConPagosActivos_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 100m });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AnularAsync(1));
    }

    [Fact]
    public async Task AnularAsync_SinPagosActivos_AnulaDesvinculaYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 100m, Activo = false });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await m.Svc.AnularAsync(1);

        m.Repo.Verify(r => r.ActualizarAsync(It.Is<Gasto>(g => !g.Activo)), Times.Once);
        m.Repo.Verify(r => r.DesvincularMovimientosAsync(1), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AnulacionGasto, "Gasto", 1, It.IsAny<string>()), Times.Once);
    }

    // ── Asociación de movimientos a factura existente ────────────────────────

    [Fact]
    public async Task AsociarMovimientosAsync_MovimientoYaFacturado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 40, Tipo = TipoMovimiento.Entrada, GastoId = 99 },
            });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => m.Svc.AsociarMovimientosAsync(1, new[] { 40 }));
    }

    [Fact]
    public async Task AsociarMovimientosAsync_Valido_AsignaYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 40, Tipo = TipoMovimiento.Entrada, GastoId = null },
            });

        await m.Svc.AsociarMovimientosAsync(1, new[] { 40 });

        m.Repo.Verify(r => r.AsignarGastoAMovimientosAsync(1,
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AsociacionMovimientosAGasto, "Gasto", 1,
            It.IsAny<string>()), Times.Once);
    }

    // ── Lecturas ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorIdAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Gasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => m.Svc.ObtenerPorIdAsync(99));
    }

    [Fact]
    public async Task ListarAsync_DelegaAlRepo()
    {
        var m = Crear();
        var filtro = new GastoFiltro(ProveedorId: 1);
        m.Repo.Setup(r => r.ListarAsync(filtro)).ReturnsAsync(new List<Gasto> { GastoValido() });

        var result = await m.Svc.ListarAsync(filtro);

        Assert.Single(result);
        m.Repo.Verify(r => r.ListarAsync(filtro), Times.Once);
    }
}
