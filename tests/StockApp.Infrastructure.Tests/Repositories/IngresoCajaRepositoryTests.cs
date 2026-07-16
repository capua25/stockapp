using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class IngresoCajaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly IngresoCajaRepository _repo;

    public IngresoCajaRepositoryTests(PostgresFixture fixture) : base(fixture)
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
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip_ConFuenteNavegable()
    {
        var fuenteId = await SeedFuenteAsync();
        var id = await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Partida mensual FIGM",
            FuenteFinanciamientoId = fuenteId, Monto = 250000.5000m,
        });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Partida mensual FIGM", found!.Concepto);
        Assert.Equal(250000.5000m, found.Monto);
        Assert.NotNull(found.FuenteFinanciamiento);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodosAsync_OrdenaFechaDesc_SinFiltrarInactivos()
    {
        var fuenteId = await SeedFuenteAsync();
        var hoy = DateTime.UtcNow;
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = hoy.AddDays(-30), Concepto = "Viejo", FuenteFinanciamientoId = fuenteId, Monto = 1m, Activo = false,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = hoy, Concepto = "Nuevo", FuenteFinanciamientoId = fuenteId, Monto = 2m,
        });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodosAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Nuevo", result[0].Concepto);   // más reciente primero
        Assert.Equal("Viejo", result[1].Concepto);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_Persiste()
    {
        var fuenteId = await SeedFuenteAsync();
        var id = await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Multas", FuenteFinanciamientoId = fuenteId, Monto = 100m,
        });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }
}
