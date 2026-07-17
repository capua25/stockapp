using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceControlPoaTests
{
    private static FinanzasVistasService Crear(out Mock<ILineaPoaRepository> lineasPoa, out Mock<IGastoRepository> gastos)
    {
        var ingresos = new Mock<IIngresoCajaRepository>();
        gastos = new Mock<IGastoRepository>();
        lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<StockApp.Application.Authorization.IAuthorizationService>();

        return new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_CalculaSaldoYPorcentaje_SinSobregiro()
    {
        var svc = Crear(out var lineasPoa, out var gastos);
        var linea = new LineaPoa
        {
            Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 1000m } },
        };
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa> { linea });
        gastos.Setup(g => g.TotalGastadoPorLineaAsync(2026))
            .ReturnsAsync(new Dictionary<int, decimal> { [1] = 400m });

        var resultado = await svc.ObtenerControlPoaAsync(2026);

        var fila = Assert.Single(resultado);
        Assert.Equal(1000m, fila.Presupuesto);
        Assert.Equal(400m, fila.Gastado);
        Assert.Equal(600m, fila.Saldo);
        Assert.Equal(40m, fila.PorcentajeEjecucion);
        Assert.False(fila.Sobregirada);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_Sobregiro_MarcaSobregiradaYSaldoNegativo()
    {
        var svc = Crear(out var lineasPoa, out var gastos);
        var linea = new LineaPoa
        {
            Id = 2, Nombre = "Prensa", Programa = "Comunicación", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 1000m } },
        };
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa> { linea });
        gastos.Setup(g => g.TotalGastadoPorLineaAsync(2026))
            .ReturnsAsync(new Dictionary<int, decimal> { [2] = 8915m });

        var resultado = await svc.ObtenerControlPoaAsync(2026);

        var fila = Assert.Single(resultado);
        Assert.Equal(-7915m, fila.Saldo);
        Assert.True(fila.Sobregirada);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_FiltraPorEjercicio()
    {
        var svc = Crear(out var lineasPoa, out var gastos);
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "2026", Programa = "P", Ejercicio = 2026 },
            new() { Id = 2, Nombre = "2025", Programa = "P", Ejercicio = 2025 },
        });
        gastos.Setup(g => g.TotalGastadoPorLineaAsync(2026)).ReturnsAsync(new Dictionary<int, decimal>());

        var resultado = await svc.ObtenerControlPoaAsync(2026);

        var fila = Assert.Single(resultado);
        Assert.Equal("2026", fila.Nombre);
    }
}
