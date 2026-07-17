using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class GastoRepositoryVistasTests : PostgresRepositoryTestBase
{
    private readonly GastoRepository _repo;

    public GastoRepositoryVistasTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new GastoRepository(Context);
    }

    private async Task<(int proveedorId, int fuenteId, int rubroId)> SeedMaestrosAsync()
    {
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro test" };
        Context.AddRange(proveedor, fuente, rubro);
        await Context.SaveChangesAsync();
        return (proveedor.Id, fuente.Id, rubro.Id);
    }

    private async Task<int> SeedLineaPoaAsync(int fuenteId, decimal asignado, int ejercicio)
    {
        var linea = new LineaPoa
        {
            Nombre = $"Linea {Guid.NewGuid():N}", Programa = "Test", Ejercicio = ejercicio,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteId, Monto = asignado } },
        };
        Context.Add(linea);
        await Context.SaveChangesAsync();
        return linea.Id;
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_TraeGastoConProveedorRubroYFuente()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Compra de prueba", Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 1000m, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), Monto = 1000m });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var desde = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var pagos = await _repo.ListarPagosActivosPorRangoAsync(desde, hasta);

        var pago = Assert.Single(pagos);
        Assert.Equal(1000m, pago.Monto);
        Assert.NotNull(pago.Gasto);
        Assert.NotNull(pago.Gasto!.Proveedor);
        Assert.NotNull(pago.Gasto.RubroGasto);
        Assert.NotNull(pago.Gasto.FuenteFinanciamiento);
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_ElPagoImpactaEnSuFechaAunqueElGastoSeaDeOtroMes()
    {
        // Regla de oro spec §4: el saldo de caja impacta en la fecha del PAGO, no de la factura.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Factura de junio, pagada en julio",
            Fecha = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), // gasto de JUNIO
            MontoTotal = 500m, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc), Monto = 500m }); // pago de JULIO
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var desdeJulio = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hastaJulio = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var pagosJulio = await _repo.ListarPagosActivosPorRangoAsync(desdeJulio, hastaJulio);

        var desdeJunio = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var hastaJunio = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
        var pagosJunio = await _repo.ListarPagosActivosPorRangoAsync(desdeJunio, hastaJunio);

        Assert.Single(pagosJulio);
        Assert.Empty(pagosJunio);
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_ExcluyePagosAnuladosYGastosAnulados()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);

        var gastoConPagoAnulado = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Pago anulado", Fecha = fecha, MontoTotal = 100m, CondicionPago = CondicionPago.Contado,
        };
        gastoConPagoAnulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 100m, Activo = false });
        await _repo.AgregarAsync(gastoConPagoAnulado);

        var gastoAnulado = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto anulado", Fecha = fecha, MontoTotal = 200m, CondicionPago = CondicionPago.Contado,
            Activo = false,
        };
        gastoAnulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 200m });
        await _repo.AgregarAsync(gastoAnulado);
        Context.ChangeTracker.Clear();

        var pagos = await _repo.ListarPagosActivosPorRangoAsync(
            fecha.AddDays(-1), fecha.AddDays(1));

        Assert.Empty(pagos);
    }

    [Fact]
    public async Task TotalPagosActivosAntesDeAsync_SumaSoloActivosDeGastosActivosAnterioresAFecha()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Compra", Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 300m, CondicionPago = CondicionPago.Contado,
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), Monto = 300m });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var total = await _repo.TotalPagosActivosAntesDeAsync(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(300m, total);
    }

    [Fact]
    public async Task ListarActivosConSaldoAsync_TraeSoloActivosConPagosYNavs()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var activo = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Activo", Fecha = DateTime.UtcNow, MontoTotal = 100m,
            CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(10),
        };
        await _repo.AgregarAsync(activo);
        var anulado = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Anulado", Fecha = DateTime.UtcNow, MontoTotal = 50m,
            CondicionPago = CondicionPago.Contado, Activo = false,
        };
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosConSaldoAsync();

        var fila = Assert.Single(resultado);
        Assert.Equal("Activo", fila.Detalle);
        Assert.NotNull(fila.Proveedor);
    }

    [Fact]
    public async Task TotalGastadoPorLineaAsync_AgrupaPorLineaYFiltraPorEjercicio()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var lineaA = await SeedLineaPoaAsync(fuenteId, 5000m, 2026);
        var lineaOtroEjercicio = await SeedLineaPoaAsync(fuenteId, 5000m, 2025);

        var gasto1 = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto 1", Fecha = DateTime.UtcNow, MontoTotal = 1000m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaA,
        };
        var gasto2 = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto 2", Fecha = DateTime.UtcNow, MontoTotal = 500m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaA,
        };
        var gastoOtroEjercicio = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto ejercicio viejo", Fecha = DateTime.UtcNow, MontoTotal = 999m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaOtroEjercicio,
        };
        await _repo.AgregarAsync(gasto1);
        await _repo.AgregarAsync(gasto2);
        await _repo.AgregarAsync(gastoOtroEjercicio);
        Context.ChangeTracker.Clear();

        var resultado = await _repo.TotalGastadoPorLineaAsync(2026);

        Assert.Equal(1500m, resultado[lineaA]);
        Assert.False(resultado.ContainsKey(lineaOtroEjercicio));
    }
}
