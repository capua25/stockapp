using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class IngresoCajaRepositoryVistasTests : PostgresRepositoryTestBase
{
    private readonly IngresoCajaRepository _repo;

    public IngresoCajaRepositoryVistasTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new IngresoCajaRepository(Context);
    }

    private async Task<int> SeedFuenteAsync()
    {
        var fuente = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        Context.Add(fuente);
        await Context.SaveChangesAsync();
        return fuente.Id;
    }

    [Fact]
    public async Task ListarPorRangoAsync_SoloTraeActivosDentroDelRango()
    {
        var fuenteId = await SeedFuenteAsync();
        var desde = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);

        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Fuera de rango (antes)", FuenteFinanciamientoId = fuenteId, Monto = 1m,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Dentro de rango", FuenteFinanciamientoId = fuenteId, Monto = 2m,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Inactivo dentro de rango", FuenteFinanciamientoId = fuenteId, Monto = 3m, Activo = false,
        });
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarPorRangoAsync(desde, hasta);

        var fila = Assert.Single(resultado);
        Assert.Equal("Dentro de rango", fila.Concepto);
        Assert.NotNull(fila.FuenteFinanciamiento);
    }

    [Fact]
    public async Task TotalActivosAntesDeAsync_SumaSoloActivosAnterioresAFecha()
    {
        var fuenteId = await SeedFuenteAsync();
        var corte = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Antes", FuenteFinanciamientoId = fuenteId, Monto = 100m,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Antes inactivo", FuenteFinanciamientoId = fuenteId, Monto = 50m, Activo = false,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Después", FuenteFinanciamientoId = fuenteId, Monto = 999m,
        });
        Context.ChangeTracker.Clear();

        var total = await _repo.TotalActivosAntesDeAsync(corte);

        Assert.Equal(100m, total);
    }

    [Fact]
    public async Task TotalActivosAntesDeAsync_SinMovimientos_DevuelveCero()
    {
        var total = await _repo.TotalActivosAntesDeAsync(DateTime.UtcNow);

        Assert.Equal(0m, total);
    }
}
