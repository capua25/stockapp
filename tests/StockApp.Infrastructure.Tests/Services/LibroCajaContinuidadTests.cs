using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

/// <summary>
/// El libro de caja con los repositorios REALES sobre Postgres: lo que un mock no puede probar es que el filtro por caja
/// y el corte de mes (el 31/07 es de julio, el 01/08 es de agosto) cierren juntos, mes a mes y contra el anual.
/// Montos en potencias de 2: cada suma tiene un único origen posible.
/// </summary>
public class LibroCajaContinuidadTests : PostgresRepositoryTestBase
{
    private readonly FinanzasVistasService _servicio;

    public LibroCajaContinuidadTests(PostgresFixture fixture) : base(fixture)
    {
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _servicio = new FinanzasVistasService(
            new IngresoCajaRepository(Context), new GastoRepository(Context), new Mock<ILineaPoaRepository>().Object,
            session.Object, new Mock<IAuthorizationService>().Object);
    }

    private static DateTime Dia(int mes, int dia) => new(2026, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Por caja: ingresos en jun/31-jul/1-ago/dic y un pago el 31/07 y otro el 15/09. Devuelve el saldo esperado
    /// de cada fin de mes para contrastarlo con el servicio.</summary>
    private async Task SembrarAsync(Caja caja, decimal escala)
    {
        var fuente = new FuenteFinanciamiento { Nombre = $"Literal {caja} {Guid.NewGuid():N}", Caja = caja };
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var rubro = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = $"Rubro {caja}" };
        Context.AddRange(fuente, proveedor, rubro);
        await Context.SaveChangesAsync();

        Context.AddRange(
            new IngresoCaja { Fecha = Dia(6, 10), Concepto = "Junio", FuenteFinanciamientoId = fuente.Id, Monto = 1000m * escala, Caja = caja },
            new IngresoCaja { Fecha = Dia(7, 31), Concepto = "31/07", FuenteFinanciamientoId = fuente.Id, Monto = 100m * escala, Caja = caja },
            new IngresoCaja { Fecha = Dia(8, 1), Concepto = "01/08", FuenteFinanciamientoId = fuente.Id, Monto = 10m * escala, Caja = caja },
            new IngresoCaja { Fecha = Dia(12, 31), Concepto = "31/12", FuenteFinanciamientoId = fuente.Id, Monto = 1m * escala, Caja = caja });
        var gasto = new Gasto
        {
            ProveedorId = proveedor.Id, RubroGastoId = rubro.Id, Detalle = $"Gasto {caja}", Fecha = Dia(7, 1),
            MontoTotal = 300m * escala, CondicionPago = CondicionPago.Credito, FechaVencimiento = Dia(10, 1),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = Dia(7, 31), Monto = 200m * escala, FuenteFinanciamientoId = fuente.Id, Caja = caja });
        gasto.Pagos.Add(new PagoGasto { Fecha = Dia(9, 15), Monto = 50m * escala, FuenteFinanciamientoId = fuente.Id, Caja = caja });
        Context.Add(gasto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(Caja.Local)]
    [InlineData(Caja.Poa)]
    public async Task Continuidad_ElSaldoFinalDeJulioEsElInicialDeAgosto_EnCadaCaja(Caja caja)
    {
        // Las dos cajas con movimiento el 31/07, en escalas distintas: si una filtrara a la otra, los saldos no darían.
        await SembrarAsync(Caja.Local, 1m);
        await SembrarAsync(Caja.Poa, 3m);
        var escala = caja == Caja.Local ? 1m : 3m;

        var julio = await _servicio.ObtenerLibroCajaMesAsync(caja, 2026, 7);
        var agosto = await _servicio.ObtenerLibroCajaMesAsync(caja, 2026, 8);

        Assert.Equal(1000m * escala, julio.SaldoInicial);
        Assert.Equal((1000m + 100m - 200m) * escala, julio.SaldoFinal);
        Assert.Equal(julio.SaldoFinal, agosto.SaldoInicial);
        Assert.Equal((1000m + 100m - 200m + 10m) * escala, agosto.SaldoFinal);
        Assert.Contains(julio.Movimientos, m => m.Concepto == "31/07");
        Assert.DoesNotContain(agosto.Movimientos, m => m.Concepto == "31/07");
        Assert.Contains(agosto.Movimientos, m => m.Concepto == "01/08");
    }

    [Theory]
    [InlineData(Caja.Local)]
    [InlineData(Caja.Poa)]
    public async Task Continuidad_CadaMesEmpiezaDondeTerminoElAnterior_YElAnualCuadraConLosMeses(Caja caja)
    {
        await SembrarAsync(Caja.Local, 1m);
        await SembrarAsync(Caja.Poa, 3m);

        var meses = new List<LibroCajaMesDto>();
        for (var mes = 1; mes <= 12; mes++)
            meses.Add(await _servicio.ObtenerLibroCajaMesAsync(caja, 2026, mes));
        var anual = await _servicio.ObtenerLibroCajaAnualAsync(caja, 2026);

        for (var i = 1; i < 12; i++)
            Assert.Equal(meses[i - 1].SaldoFinal, meses[i].SaldoInicial);
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(meses[i].Movimientos.Sum(m => m.Ingreso), anual.TotalesPorMes[i].Ingresos);
            Assert.Equal(meses[i].Movimientos.Sum(m => m.Egreso), anual.TotalesPorMes[i].Egresos);
        }
        Assert.Equal(meses[11].SaldoFinal, meses[0].SaldoInicial + anual.TotalesPorMes.Sum(t => t.Neto));
        var escala = caja == Caja.Local ? 1m : 3m;
        Assert.Equal((1000m + 100m + 10m + 1m - 200m - 50m) * escala, meses[11].SaldoFinal);
    }
}
