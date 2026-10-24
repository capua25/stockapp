using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceLibroCajaAnualTests
{
    private static FinanzasVistasService Crear(
        out Mock<IIngresoCajaRepository> ingresos, out Mock<IGastoRepository> gastos)
    {
        ingresos = new Mock<IIngresoCajaRepository>();
        gastos = new Mock<IGastoRepository>();
        var lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<StockApp.Application.Authorization.IAuthorizationService>();

        return new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
    }

    [Fact]
    public async Task ObtenerLibroCajaAnualAsync_AgrupaIngresosYEgresosPorMes()
    {
        var svc = Crear(out var ingresos, out var gastos);
        ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), Concepto = "Enero", Monto = 100m },
                new() { Fecha = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), Concepto = "Marzo", Monto = 50m },
            });
        var rubro = new RubroGasto { Id = 1, Nombre = "Obras" };
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), Monto = 30m,
                        Gasto = new Gasto { RubroGasto = rubro } },
            });

        var resultado = await svc.ObtenerLibroCajaAnualAsync(Caja.Local, 2026);

        Assert.Equal(12, resultado.TotalesPorMes.Count);
        var enero = resultado.TotalesPorMes.Single(m => m.Mes == 1);
        Assert.Equal(100m, enero.Ingresos);
        Assert.Equal(30m, enero.Egresos);
        Assert.Equal(70m, enero.Neto);
        var marzo = resultado.TotalesPorMes.Single(m => m.Mes == 3);
        Assert.Equal(50m, marzo.Ingresos);
        Assert.Equal(0m, marzo.Egresos);
        Assert.Contains(resultado.TotalesPorRubro, t => t.Clave == "Obras" && t.Total == 30m);
    }

    [Fact]
    public async Task Anual_CajaPoa_SoloPoa()
    {
        var svc = Crear(out var ingresos, out var gastos);
        var fecha = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        var rubro = new RubroGasto { Id = 1, Nombre = "Obras" };
        ingresos.Setup(i => i.ListarPorRangoAsync(Caja.Poa, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja> { new() { Fecha = fecha, Concepto = "POA", Monto = 7m, Caja = Caja.Poa } });
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(Caja.Poa, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = fecha, Monto = 2m, Caja = Caja.Poa, Gasto = new Gasto { RubroGasto = rubro } },
            });
        ingresos.Setup(i => i.ListarPorRangoAsync(Caja.Local, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja> { new() { Fecha = fecha, Concepto = "Local", Monto = 700m } });
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(Caja.Local, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = fecha, Monto = 200m, Gasto = new Gasto { RubroGasto = rubro } },
            });

        var resultado = await svc.ObtenerLibroCajaAnualAsync(Caja.Poa, 2026);

        var febrero = resultado.TotalesPorMes.Single(m => m.Mes == 2);
        Assert.Equal(7m, febrero.Ingresos);
        Assert.Equal(2m, febrero.Egresos);
        Assert.Equal(5m, febrero.Neto);
        Assert.Equal(new[] { ("Obras", 2m) }, resultado.TotalesPorRubro.Select(t => (t.Clave, t.Total)));
    }

    [Fact]
    public async Task Anual_CajaInexistente_LanzaArgumentException()
    {
        var svc = Crear(out var ingresos, out var gastos);
        // Los dos repositorios responden listas vacías: sin la validación, el anual terminaría bien y el test caería
        // por "no se lanzó nada", no por un NullReference del mock sin configurar.
        ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>());
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>());

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ObtenerLibroCajaAnualAsync((Caja)7, 2026));

        ingresos.Verify(i => i.ListarPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
        gastos.Verify(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<Caja>(), It.IsAny<DateTime>(), It.IsAny<DateTime>()), Times.Never);
    }
}
