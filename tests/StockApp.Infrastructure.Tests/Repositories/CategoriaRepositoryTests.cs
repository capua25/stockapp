using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class CategoriaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly CategoriaRepository _repo;

    public CategoriaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new CategoriaRepository(Context);
    }

    private static Categoria NuevaCategoria(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    // ── roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var cat = NuevaCategoria("Bebidas");
        var id = await _repo.AgregarAsync(cat);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Bebidas", found!.Nombre);
        Assert.True(found.Activo);
    }

    // ── ListarTodasAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevaCategoria("Activa1", activo: true));
        await _repo.AgregarAsync(NuevaCategoria("Activa2", activo: true));
        await _repo.AgregarAsync(NuevaCategoria("Inactiva", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        // ListarTodasAsync devuelve todas (activas e inactivas) — la UI filtra si quiere
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevaCategoria("Zapatos"));
        await _repo.AgregarAsync(NuevaCategoria("Alimentos"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Alimentos", result[0].Nombre);
        Assert.Equal("Zapatos", result[1].Nombre);
    }

    // ── ExisteNombreAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaCategoria("Bebidas"));

        Assert.True(await _repo.ExisteNombreAsync("Bebidas"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaCategoria_RetornaFalse()
    {
        var cat = NuevaCategoria("Bebidas");
        var id = await _repo.AgregarAsync(cat);

        // La misma categoría con su propio id excluido → no hay duplicado
        Assert.False(await _repo.ExisteNombreAsync("Bebidas", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtraTieneMismoNombre_RetornaTrue()
    {
        var cat1 = NuevaCategoria("Bebidas");
        var cat2 = NuevaCategoria("Alimentos");
        await _repo.AgregarAsync(cat1);
        var id2 = await _repo.AgregarAsync(cat2);

        // cat2 intenta renombrarse a "Bebidas" — ya existe en cat1
        Assert.True(await _repo.ExisteNombreAsync("Bebidas", excluyendoId: id2));
    }

    // ── ActualizarAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var cat = NuevaCategoria("Bebidas");
        var id = await _repo.AgregarAsync(cat);
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
        var cat = NuevaCategoria("Nombre Original");
        var id = await _repo.AgregarAsync(cat);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
