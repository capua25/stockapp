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
    public async Task ListarPagosActivosPorRangoAsync_TraeGastoConProveedorRubroYLaFuenteDelPago()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Compra de prueba", Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 1000m, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), Monto = 1000m, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var desde = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var pagos = await _repo.ListarPagosActivosPorRangoAsync(Caja.Local, desde, hasta);

        var pago = Assert.Single(pagos);
        Assert.Equal(1000m, pago.Monto);
        Assert.NotNull(pago.Gasto);
        Assert.NotNull(pago.Gasto!.Proveedor);
        Assert.NotNull(pago.Gasto.RubroGasto);
        Assert.NotNull(pago.FuenteFinanciamiento);
    }

    [Fact]
    public async Task ListarPagosActivosPorRango_IncluyeLaFuenteDelPago()
    {
        // El gasto ya no tiene fuente: cada pago trae la suya (dos literales de cajas distintas en el mismo gasto).
        var (proveedorId, fuenteLocalId, rubroId) = await SeedMaestrosAsync();
        var poa = new FuenteFinanciamiento { Nombre = $"Literal POA {Guid.NewGuid():N}", Caja = Caja.Poa };
        Context.Add(poa);
        await Context.SaveChangesAsync();
        var fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId, Detalle = "Compra", Fecha = fecha,
            MontoTotal = 300m, CondicionPago = CondicionPago.Credito, FechaVencimiento = fecha.AddDays(30),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 100m, FuenteFinanciamientoId = fuenteLocalId });
        gasto.Pagos.Add(new PagoGasto { Fecha = fecha.AddDays(1), Monto = 200m, FuenteFinanciamientoId = poa.Id });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var pagos = await _repo.ListarPagosActivosPorRangoAsync(Caja.Local,
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(2, pagos.Count);
        Assert.All(pagos, p => Assert.NotNull(p.FuenteFinanciamiento));
        Assert.Equal(poa.Nombre, pagos.Single(p => p.Monto == 200m).FuenteFinanciamiento!.Nombre);
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_ElPagoImpactaEnSuFechaAunqueElGastoSeaDeOtroMes()
    {
        // Regla de oro spec §4: el saldo de caja impacta en la fecha del PAGO, no de la factura.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Factura de junio, pagada en julio",
            Fecha = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), // gasto de JUNIO
            MontoTotal = 500m, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc), Monto = 500m, FuenteFinanciamientoId = fuenteId }); // pago de JULIO
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var desdeJulio = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hastaJulio = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var pagosJulio = await _repo.ListarPagosActivosPorRangoAsync(Caja.Local, desdeJulio, hastaJulio);

        var desdeJunio = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var hastaJunio = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
        var pagosJunio = await _repo.ListarPagosActivosPorRangoAsync(Caja.Local, desdeJunio, hastaJunio);

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
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Pago anulado", Fecha = fecha, MontoTotal = 100m, CondicionPago = CondicionPago.Contado,
        };
        gastoConPagoAnulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 100m, Activo = false, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(gastoConPagoAnulado);

        var gastoAnulado = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Gasto anulado", Fecha = fecha, MontoTotal = 200m, CondicionPago = CondicionPago.Contado,
            Activo = false,
        };
        gastoAnulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 200m, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(gastoAnulado);
        Context.ChangeTracker.Clear();

        var pagos = await _repo.ListarPagosActivosPorRangoAsync(Caja.Local,
            fecha.AddDays(-1), fecha.AddDays(1));

        Assert.Empty(pagos);
    }

    [Fact]
    public async Task TotalPagosActivosAntesDeAsync_SumaSoloActivosDeGastosActivosAnterioresAFecha()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Compra", Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 300m, CondicionPago = CondicionPago.Contado,
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), Monto = 300m, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var total = await _repo.TotalPagosActivosAntesDeAsync(Caja.Local, new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(300m, total);
    }

    private static Gasto GastoCredito(int proveedorId, int rubroId, string detalle, DateTime fecha, bool activo = true)
        => new()
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId, Detalle = detalle, Fecha = fecha,
            MontoTotal = 1000m, CondicionPago = CondicionPago.Credito, FechaVencimiento = fecha.AddDays(30), Activo = activo,
        };

    [Fact]
    public async Task ListarPagosActivosPorRango_SoloLosDeLaCaja()
    {
        // Montos potencias de 2: cada suma tiene un único origen posible.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var activo = GastoCredito(proveedorId, rubroId, "Activo", fecha);
        activo.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 1m, Caja = Caja.Local, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(PagoGasto.Automatico(fecha, 2m, "automático POA", Caja.Poa, fuenteId));
        activo.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 4m, Caja = Caja.Poa, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 8m, Caja = Caja.Poa, Activo = false, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 16m, Caja = Caja.Local, Activo = false, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(activo);
        var anulado = GastoCredito(proveedorId, rubroId, "Gasto anulado", fecha, activo: false);
        anulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 32m, Caja = Caja.Poa, FuenteFinanciamientoId = fuenteId });
        anulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 64m, Caja = Caja.Local, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        var desde = fecha.AddDays(-1);
        var hasta = fecha.AddDays(1);
        var poa = await _repo.ListarPagosActivosPorRangoAsync(Caja.Poa, desde, hasta);
        var local = await _repo.ListarPagosActivosPorRangoAsync(Caja.Local, desde, hasta);

        Assert.Equal(6m, poa.Sum(p => p.Monto));
        Assert.Equal(2, poa.Count);
        Assert.Equal(1m, Assert.Single(local).Monto);
    }

    [Fact]
    public async Task TotalActivosAntesDe_SoloLosDeLaCaja()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var antes = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var corte = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var activo = GastoCredito(proveedorId, rubroId, "Activo", antes);
        activo.Pagos.Add(new PagoGasto { Fecha = antes, Monto = 1m, Caja = Caja.Local, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(PagoGasto.Automatico(antes, 2m, "automático POA", Caja.Poa, fuenteId));
        activo.Pagos.Add(new PagoGasto { Fecha = antes, Monto = 4m, Caja = Caja.Poa, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(new PagoGasto { Fecha = antes, Monto = 8m, Caja = Caja.Poa, Activo = false, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(new PagoGasto { Fecha = antes, Monto = 16m, Caja = Caja.Local, Activo = false, FuenteFinanciamientoId = fuenteId });
        activo.Pagos.Add(new PagoGasto { Fecha = corte.AddDays(3), Monto = 32m, Caja = Caja.Poa, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(activo);
        var anulado = GastoCredito(proveedorId, rubroId, "Gasto anulado", antes, activo: false);
        anulado.Pagos.Add(new PagoGasto { Fecha = antes, Monto = 64m, Caja = Caja.Poa, FuenteFinanciamientoId = fuenteId });
        anulado.Pagos.Add(new PagoGasto { Fecha = antes, Monto = 128m, Caja = Caja.Local, FuenteFinanciamientoId = fuenteId });
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        Assert.Equal(6m, await _repo.TotalPagosActivosAntesDeAsync(Caja.Poa, corte));
        Assert.Equal(1m, await _repo.TotalPagosActivosAntesDeAsync(Caja.Local, corte));
    }

    [Fact]
    public async Task ListarActivosConSaldoAsync_TraeSoloActivosConPagosYNavs()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var activo = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Activo", Fecha = DateTime.UtcNow, MontoTotal = 100m,
            CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(10),
        };
        await _repo.AgregarAsync(activo);
        var anulado = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
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
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Gasto 1", Fecha = DateTime.UtcNow, MontoTotal = 1000m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaA,
        };
        var gasto2 = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
            Detalle = "Gasto 2", Fecha = DateTime.UtcNow, MontoTotal = 500m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaA,
        };
        var gastoOtroEjercicio = new Gasto
        {
            ProveedorId = proveedorId, RubroGastoId = rubroId,
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
