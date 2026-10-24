using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceLibroCajaTests
{
    private sealed record Mocks(
        FinanzasVistasService Svc,
        Mock<IIngresoCajaRepository> Ingresos,
        Mock<IGastoRepository> Gastos,
        Mock<ILineaPoaRepository> LineasPoa);

    private static Mocks Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var ingresos = new Mock<IIngresoCajaRepository>();
        var gastos = new Mock<IGastoRepository>();
        var lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);

        ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>());
        ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>());
        gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);

        var svc = new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
        return new Mocks(svc, ingresos, gastos, lineasPoa);
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_SinPermiso_LanzaExcepcionDeAutorizacion()
    {
        var m = Crear();
        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.VerFinanzas))
            .Throws(new UnauthorizedAccessException());
        var svc = new FinanzasVistasService(
            m.Ingresos.Object, m.Gastos.Object, m.LineasPoa.Object,
            Mock.Of<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), auth.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 7));
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_MesFueraDeRango_LanzaArgumentException()
    {
        var m = Crear();

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 13));
        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 0));
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_SinMovimientos_SaldoInicialIgualASaldoFinal()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(1000m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(400m);

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 7);

        Assert.Equal(600m, resultado.SaldoInicial);
        Assert.Equal(600m, resultado.SaldoFinal);
        Assert.Empty(resultado.Movimientos);
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_ConIngresosYEgresos_CalculaSaldoCorridoCronologico()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);

        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Literal B" };
        var rubro = new RubroGasto { Id = 1, Nombre = "Obras" };
        var proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" };
        var gastoDelPago = new Gasto
        {
            Id = 1, Detalle = "Compra", Proveedor = proveedor, RubroGasto = rubro,
            NumeroFactura = "A-1", Activo = true,
        };

        m.Ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                        Concepto = "Partida FIGM", FuenteFinanciamiento = fuente, Monto = 1000m },
            });
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
                        Monto = 300m, Gasto = gastoDelPago, FuenteFinanciamiento = fuente },
            });

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 7);

        Assert.Equal(0m, resultado.SaldoInicial);
        Assert.Equal(2, resultado.Movimientos.Count);
        Assert.Equal("Ingreso", resultado.Movimientos[0].Tipo);
        Assert.Equal(1000m, resultado.Movimientos[0].SaldoCorrido);
        Assert.Equal("Egreso", resultado.Movimientos[1].Tipo);
        Assert.Equal("Barraca X", resultado.Movimientos[1].ProveedorNombre);
        Assert.Equal(700m, resultado.Movimientos[1].SaldoCorrido);
        Assert.Equal(700m, resultado.SaldoFinal);
        Assert.Contains(resultado.TotalesPorRubro, t => t.Clave == "Obras" && t.Total == 300m);
    }

    [Fact]
    public async Task Mes_MismoDia_IngresoVaAntesQueEgresoYElSaldoCorridoNoPasaPorNegativo()
    {
        var m = Crear();
        var dia = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var gasto = new Gasto { Id = 1, Detalle = "Compra", Activo = true };
        m.Ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja> { new() { Fecha = dia, Concepto = "Partida", Monto = 1000m } });
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto> { new() { Fecha = dia, Monto = 500m, Gasto = gasto } });

        var r = await m.Svc.ObtenerLibroCajaMesAsync(Caja.Poa, 2026, 7);

        Assert.Equal(new[] { "Ingreso", "Egreso" }, r.Movimientos.Select(x => x.Tipo));
        Assert.Equal(new[] { 1000m, 500m }, r.Movimientos.Select(x => x.SaldoCorrido));
        Assert.Equal(500m, r.SaldoFinal);
    }

    [Fact]
    public async Task Mes_FuenteNombreYTotalesPorFuente_SonLosDelPago()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);
        m.Ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>());

        // El gasto ya no tiene fuente: la del libro es la del PAGO (dos pagos de literales distintos).
        var gasto = new Gasto
        {
            Id = 1, Detalle = "Compra", Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
            RubroGasto = new RubroGasto { Id = 1, Nombre = "Obras" }, Activo = true,
        };
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), Monto = 300m, Gasto = gasto,
                        FuenteFinanciamientoId = 1, FuenteFinanciamiento = new FuenteFinanciamiento { Id = 1, Nombre = "Literal A" } },
                new() { Fecha = new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc), Monto = 200m, Gasto = gasto,
                        FuenteFinanciamientoId = 2, FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B" } },
            });

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 7);

        Assert.Equal("Literal A", resultado.Movimientos[0].FuenteNombre);
        Assert.Equal("Literal B", resultado.Movimientos[1].FuenteNombre);
        Assert.Contains(resultado.TotalesPorFuente, t => t.Clave == "Literal A" && t.Total == -300m);
        Assert.Contains(resultado.TotalesPorFuente, t => t.Clave == "Literal B" && t.Total == -200m);
        Assert.DoesNotContain(resultado.TotalesPorFuente, t => t.Clave == "(sin fuente)");
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_SaldoPuedeQuedarNegativo()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(100m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>())).ReturnsAsync(0m);
        var gasto = new Gasto { Id = 1, Detalle = "Compra grande", Activo = true };
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), Monto = 500m, Gasto = gasto },
            });

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(Caja.Local, 2026, 7);

        Assert.Equal(-400m, resultado.SaldoFinal);
    }

    [Fact]
    public async Task Mes_CajaPoa_SaldoInicialMovimientosYTotalesSoloPoa()
    {
        // Cada caja devuelve datos distintos: si el servicio consulta la caja equivocada, los números no cierran.
        var m = Crear();
        var fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var literalPoa = new FuenteFinanciamiento { Id = 9, Nombre = "Literal POA", Caja = Caja.Poa };
        var literalLocal = new FuenteFinanciamiento { Id = 1, Nombre = "Literal Local" };
        var gasto = new Gasto
        {
            Id = 1, Detalle = "Compra", Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
            RubroGasto = new RubroGasto { Id = 1, Nombre = "Obras" }, Activo = true,
        };

        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(Caja.Poa, It.IsAny<DateTime>())).ReturnsAsync(1000m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(Caja.Poa, It.IsAny<DateTime>())).ReturnsAsync(100m);
        m.Ingresos.Setup(i => i.ListarPorRangoAsync(Caja.Poa, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = fecha, Concepto = "Partida POA", FuenteFinanciamiento = literalPoa, Monto = 20m, Caja = Caja.Poa },
            });
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(Caja.Poa, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = fecha.AddDays(1), Monto = 3m, Gasto = gasto, Caja = Caja.Poa, FuenteFinanciamiento = literalPoa },
            });

        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(Caja.Local, It.IsAny<DateTime>())).ReturnsAsync(40000m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(Caja.Local, It.IsAny<DateTime>())).ReturnsAsync(4000m);
        m.Ingresos.Setup(i => i.ListarPorRangoAsync(Caja.Local, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = fecha, Concepto = "Partida Local", FuenteFinanciamiento = literalLocal, Monto = 500000m },
            });
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(Caja.Local, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = fecha, Monto = 6000000m, Gasto = gasto, FuenteFinanciamiento = literalLocal },
            });

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(Caja.Poa, 2026, 7);

        Assert.Equal(900m, resultado.SaldoInicial);
        Assert.Equal(917m, resultado.SaldoFinal);
        Assert.Equal(new[] { "Partida POA", "Compra" }, resultado.Movimientos.Select(x => x.Concepto));
        Assert.Equal(new[] { 920m, 917m }, resultado.Movimientos.Select(x => x.SaldoCorrido));
        Assert.Equal(new[] { ("Obras", 3m) }, resultado.TotalesPorRubro.Select(t => (t.Clave, t.Total)));
        Assert.Equal(new[] { ("Literal POA", 17m) }, resultado.TotalesPorFuente.Select(t => (t.Clave, t.Total)));
    }

    [Fact]
    public async Task Mes_CajaInexistente_LanzaArgumentException()
    {
        var m = Crear();

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.ObtenerLibroCajaMesAsync((Caja)7, 2026, 7));

        m.Ingresos.Verify(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        m.Gastos.Verify(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<Caja>(), It.IsAny<DateTime>()), Times.Never);
    }
}
