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
        ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), Concepto = "Enero", Monto = 100m },
                new() { Fecha = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), Concepto = "Marzo", Monto = 50m },
            });
        var rubro = new RubroGasto { Id = 1, Nombre = "Obras" };
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), Monto = 30m,
                        Gasto = new Gasto { RubroGasto = rubro } },
            });

        var resultado = await svc.ObtenerLibroCajaAnualAsync(2026);

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
}
