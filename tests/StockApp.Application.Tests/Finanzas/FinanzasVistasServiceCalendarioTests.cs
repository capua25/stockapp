using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceCalendarioTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static FinanzasVistasService Crear(out Mock<IGastoRepository> gastos)
    {
        var ingresos = new Mock<IIngresoCajaRepository>();
        gastos = new Mock<IGastoRepository>();
        var lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<StockApp.Application.Authorization.IAuthorizationService>();

        return new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
    }

    private static Gasto GastoCredito(int id, DateTime vencimiento, string proveedor = "Barraca X") => new()
    {
        Id = id, Detalle = "Compra", MontoTotal = 1000m, Fecha = Hoy.AddDays(-30),
        CondicionPago = CondicionPago.Credito, FechaVencimiento = vencimiento,
        Proveedor = new Proveedor { Id = 1, Nombre = proveedor },
    };

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_ClasificaVencidaAVencer7YAVencer30()
    {
        var svc = Crear(out var gastos);
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto>
        {
            GastoCredito(1, Hoy.AddDays(-1), "Vencida"),
            GastoCredito(2, Hoy.AddDays(5), "AVencer7"),
            GastoCredito(3, Hoy.AddDays(20), "AVencer30"),
            GastoCredito(4, Hoy.AddDays(60), "FueraDeRango"),
        });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        Assert.Single(resultado.Vencidas);
        Assert.Equal("Vencida", resultado.Vencidas[0].ProveedorNombre);
        Assert.Single(resultado.AVencer7Dias);
        Assert.Equal("AVencer7", resultado.AVencer7Dias[0].ProveedorNombre);
        Assert.Single(resultado.AVencer30Dias);
        Assert.Equal("AVencer30", resultado.AVencer30Dias[0].ProveedorNombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_ExcluyeGastosYaPagados()
    {
        var svc = Crear(out var gastos);
        var pagado = GastoCredito(1, Hoy.AddDays(-1));
        pagado.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-10), Monto = 1000m });
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto> { pagado });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        Assert.Empty(resultado.Vencidas);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_PagosRecientes_SoloUltimos7Dias()
    {
        var svc = Crear(out var gastos);
        var gasto = new Gasto
        {
            Id = 1, Detalle = "Compra", MontoTotal = 500m, CondicionPago = CondicionPago.Contado,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-3), Monto = 500m });
        var gastoViejo = new Gasto
        {
            Id = 2, Detalle = "Compra vieja", MontoTotal = 200m, CondicionPago = CondicionPago.Contado,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca Y" },
        };
        gastoViejo.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-20), Monto = 200m });
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto> { gasto, gastoViejo });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        var pago = Assert.Single(resultado.PagosRecientes);
        Assert.Equal("Barraca X", pago.ProveedorNombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_VenceExactamenteA7Dias_SoloEnAVencer7Dias()
    {
        var svc = Crear(out var gastos);
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto>
        {
            GastoCredito(1, Hoy.AddDays(7), "Exacto7"),
        });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        Assert.Empty(resultado.Vencidas);
        Assert.Single(resultado.AVencer7Dias);
        Assert.Equal("Exacto7", resultado.AVencer7Dias[0].ProveedorNombre);
        Assert.Empty(resultado.AVencer30Dias);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_VenceExactamenteA30Dias_SoloEnAVencer30Dias()
    {
        var svc = Crear(out var gastos);
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto>
        {
            GastoCredito(1, Hoy.AddDays(30), "Exacto30"),
        });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        Assert.Empty(resultado.Vencidas);
        Assert.Empty(resultado.AVencer7Dias);
        Assert.Single(resultado.AVencer30Dias);
        Assert.Equal("Exacto30", resultado.AVencer30Dias[0].ProveedorNombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_SinFechaReferencia_UsaUtcNow()
    {
        var svc = Crear(out var gastos);
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto>
        {
            GastoCredito(1, DateTime.UtcNow.AddDays(-1)),
        });

        var resultado = await svc.ObtenerCalendarioPagosAsync();

        Assert.Single(resultado.Vencidas);
    }
}
