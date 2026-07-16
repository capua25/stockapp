using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class FuenteFinanciamientoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly FuenteFinanciamientoRepository _repo;

    public FuenteFinanciamientoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new FuenteFinanciamientoRepository(Context);
    }

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal B" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Literal B", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasOrdenadasPorNombre_SinFiltroDeActivo()
    {
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Multas" });
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal A", Activo = false });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Literal A", result[0].Nombre);
        Assert.Equal("Multas", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal C" });

        Assert.True(await _repo.ExisteNombreAsync("Literal C"));
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaFuente_RetornaFalse()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal C" });

        Assert.False(await _repo.ExisteNombreAsync("Literal C", excluyendoId: id));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_Persiste()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Excedentes" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }
}
