using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class RubroGastoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly RubroGastoRepository _repo;

    public RubroGastoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new RubroGastoRepository(Context);
    }

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var id = await _repo.AgregarAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(3, found!.Codigo);
        Assert.Equal("Combustibles", found.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodosAsync_RetornaOrdenadosPorCodigo()
    {
        await _repo.AgregarAsync(new RubroGasto { Codigo = 9, Nombre = "Eventos" });
        await _repo.AgregarAsync(new RubroGasto { Codigo = 1, Nombre = "Sueldos" });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodosAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Codigo);
        Assert.Equal(9, result[1].Codigo);
    }

    [Fact]
    public async Task ExisteCodigoAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(new RubroGasto { Codigo = 5, Nombre = "Papelería" });

        Assert.True(await _repo.ExisteCodigoAsync(5));
        Assert.False(await _repo.ExisteCodigoAsync(99));
    }

    [Fact]
    public async Task ExisteCodigoAsync_ExcluyendoId_MismoRubro_RetornaFalse()
    {
        var id = await _repo.AgregarAsync(new RubroGasto { Codigo = 5, Nombre = "Papelería" });

        Assert.False(await _repo.ExisteCodigoAsync(5, excluyendoId: id));
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombreYBajaLogica_Persiste()
    {
        var id = await _repo.AgregarAsync(new RubroGasto { Codigo = 7, Nombre = "Original" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Modificado";
        found.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Modificado", updated!.Nombre);
        Assert.False(updated.Activo);
    }
}
