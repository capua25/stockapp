using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class DimensionTematicaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly DimensionTematicaRepository _repo;

    public DimensionTematicaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new DimensionTematicaRepository(Context);
    }

    private static DimensionTematica NuevaDimension(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var dimension = NuevaDimension("Infraestructura");
        var id = await _repo.AgregarAsync(dimension);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Infraestructura", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevaDimension("Infraestructura", activo: true));
        await _repo.AgregarAsync(NuevaDimension("Tránsito", activo: true));
        await _repo.AgregarAsync(NuevaDimension("Discontinuada", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevaDimension("Tránsito"));
        await _repo.AgregarAsync(NuevaDimension("Infraestructura"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Infraestructura", result[0].Nombre);
        Assert.Equal("Tránsito", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaDimension("Infraestructura"));

        Assert.True(await _repo.ExisteNombreAsync("Infraestructura"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaDimension_RetornaFalse()
    {
        var dimension = NuevaDimension("Infraestructura");
        var id = await _repo.AgregarAsync(dimension);

        Assert.False(await _repo.ExisteNombreAsync("Infraestructura", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtraTieneMismoNombre_RetornaTrue()
    {
        var dimension1 = NuevaDimension("Infraestructura");
        var dimension2 = NuevaDimension("Tránsito");
        await _repo.AgregarAsync(dimension1);
        var id2 = await _repo.AgregarAsync(dimension2);

        Assert.True(await _repo.ExisteNombreAsync("Infraestructura", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var dimension = NuevaDimension("Infraestructura");
        var id = await _repo.AgregarAsync(dimension);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombre_Persiste()
    {
        var dimension = NuevaDimension("Nombre Original");
        var id = await _repo.AgregarAsync(dimension);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
