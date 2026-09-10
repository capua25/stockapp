using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class OrganismoResponsableRepositoryTests : PostgresRepositoryTestBase
{
    private readonly OrganismoResponsableRepository _repo;

    public OrganismoResponsableRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new OrganismoResponsableRepository(Context);
    }

    private static OrganismoResponsable NuevoOrganismo(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var organismo = NuevoOrganismo("Intendencia de Colonia");
        var id = await _repo.AgregarAsync(organismo);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Intendencia de Colonia", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia", activo: true));
        await _repo.AgregarAsync(NuevoOrganismo("Municipio de Carmelo", activo: true));
        await _repo.AgregarAsync(NuevoOrganismo("Organismo Inactivo", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Municipio de Carmelo"));
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Intendencia de Colonia", result[0].Nombre);
        Assert.Equal("Municipio de Carmelo", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia"));

        Assert.True(await _repo.ExisteNombreAsync("Intendencia de Colonia"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismoOrganismo_RetornaFalse()
    {
        var organismo = NuevoOrganismo("Intendencia de Colonia");
        var id = await _repo.AgregarAsync(organismo);

        Assert.False(await _repo.ExisteNombreAsync("Intendencia de Colonia", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtroTieneMismoNombre_RetornaTrue()
    {
        var organismo1 = NuevoOrganismo("Intendencia de Colonia");
        var organismo2 = NuevoOrganismo("Municipio de Carmelo");
        await _repo.AgregarAsync(organismo1);
        var id2 = await _repo.AgregarAsync(organismo2);

        Assert.True(await _repo.ExisteNombreAsync("Intendencia de Colonia", excluyendoId: id2));
    }

    // ── Normalización case-insensitive (LOWER) ───────────────────────────────

    [Fact]
    public async Task ExisteNombreAsync_DiferenteCasing_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia"));

        Assert.True(await _repo.ExisteNombreAsync("intendencia de colonia"));
    }

    [Fact]
    public async Task IndiceDeBase_RechazaDuplicadoPorCasing_AunSinPasarPorElRepositorio()
    {
        Context.OrganismosResponsables.Add(NuevoOrganismo("Intendencia de Colonia"));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.OrganismosResponsables.Add(NuevoOrganismo("intendencia de colonia"));

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var organismo = NuevoOrganismo("Intendencia de Colonia");
        var id = await _repo.AgregarAsync(organismo);
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
        var organismo = NuevoOrganismo("Nombre Original");
        var id = await _repo.AgregarAsync(organismo);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }

    [Fact]
    public async Task ActualizarAsync_SoloCambiaCasing_PersisteYElIndiceFuncionalNoLoRechaza()
    {
        var id = await _repo.AgregarAsync(NuevoOrganismo("intendencia de colonia"));
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Intendencia de Colonia";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Intendencia de Colonia", updated!.Nombre);
    }
}
