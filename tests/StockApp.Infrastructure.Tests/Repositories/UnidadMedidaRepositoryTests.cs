using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class UnidadMedidaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly UnidadMedidaRepository _repo;

    public UnidadMedidaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new UnidadMedidaRepository(Context);
    }

    private static UnidadMedida NuevaUm(string nombre, string abreviatura, bool activo = true) =>
        new() { Nombre = nombre, Abreviatura = abreviatura, Activo = activo };

    // ── roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var um = NuevaUm("Kilogramo", "kg");
        var id = await _repo.AgregarAsync(um);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Kilogramo", found!.Nombre);
        Assert.Equal("kg", found.Abreviatura);
        Assert.True(found.Activo);
    }

    // ── ListarTodasAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevaUm("Metro", "m"));
        await _repo.AgregarAsync(NuevaUm("Kilo", "kg"));
        await _repo.AgregarAsync(NuevaUm("Inactiva", "x", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        // ListarTodasAsync devuelve todas (activas e inactivas), ordenadas por Nombre
        Assert.Equal(3, result.Count);
        Assert.Equal("Inactiva", result[0].Nombre);
        Assert.Equal("Kilo", result[1].Nombre);
        Assert.Equal("Metro", result[2].Nombre);
    }

    // ── ExisteNombreAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaUm("Litro", "l"));

        Assert.True(await _repo.ExisteNombreAsync("Litro"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("Parsec"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaUm_RetornaFalse()
    {
        var um = NuevaUm("Litro", "l");
        var id = await _repo.AgregarAsync(um);

        Assert.False(await _repo.ExisteNombreAsync("Litro", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtraTieneMismoNombre_RetornaTrue()
    {
        var um1 = NuevaUm("Litro", "l");
        var um2 = NuevaUm("Metro", "m");
        await _repo.AgregarAsync(um1);
        var id2 = await _repo.AgregarAsync(um2);

        Assert.True(await _repo.ExisteNombreAsync("Litro", excluyendoId: id2));
    }

    // ── ExisteAbreviaturaAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ExisteAbreviaturaAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaUm("Kilogramo", "kg"));

        Assert.True(await _repo.ExisteAbreviaturaAsync("kg"));
    }

    [Fact]
    public async Task ExisteAbreviaturaAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteAbreviaturaAsync("xyz"));
    }

    [Fact]
    public async Task ExisteAbreviaturaAsync_ExcluyendoId_MismaUm_RetornaFalse()
    {
        var um = NuevaUm("Kilogramo", "kg");
        var id = await _repo.AgregarAsync(um);

        Assert.False(await _repo.ExisteAbreviaturaAsync("kg", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteAbreviaturaAsync_ExcluyendoId_OtraTieneMismaAbreviatura_RetornaTrue()
    {
        var um1 = NuevaUm("Kilogramo", "kg");
        var um2 = NuevaUm("Metro", "m");
        await _repo.AgregarAsync(um1);
        var id2 = await _repo.AgregarAsync(um2);

        // um2 quiere usar "kg" que ya está en um1
        Assert.True(await _repo.ExisteAbreviaturaAsync("kg", excluyendoId: id2));
    }

    // ── ActualizarAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var um = NuevaUm("Litro", "l");
        var id = await _repo.AgregarAsync(um);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaAbreviatura_Persiste()
    {
        var um = NuevaUm("Kilogramo", "kg");
        var id = await _repo.AgregarAsync(um);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Abreviatura = "KG";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("KG", updated!.Abreviatura);
    }
}
