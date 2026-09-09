using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ZonaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ZonaRepository _repo;

    public ZonaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ZonaRepository(Context);
    }

    private static Zona NuevaZona(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var zona = NuevaZona("Centro");
        var id = await _repo.AgregarAsync(zona);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Centro", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevaZona("Centro", activo: true));
        await _repo.AgregarAsync(NuevaZona("Norte", activo: true));
        await _repo.AgregarAsync(NuevaZona("Sur Inactiva", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevaZona("Zona Sur"));
        await _repo.AgregarAsync(NuevaZona("Centro"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Centro", result[0].Nombre);
        Assert.Equal("Zona Sur", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaZona("Centro"));

        Assert.True(await _repo.ExisteNombreAsync("Centro"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaZona_RetornaFalse()
    {
        var zona = NuevaZona("Centro");
        var id = await _repo.AgregarAsync(zona);

        Assert.False(await _repo.ExisteNombreAsync("Centro", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtraTieneMismoNombre_RetornaTrue()
    {
        var zona1 = NuevaZona("Centro");
        var zona2 = NuevaZona("Norte");
        await _repo.AgregarAsync(zona1);
        var id2 = await _repo.AgregarAsync(zona2);

        Assert.True(await _repo.ExisteNombreAsync("Centro", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var zona = NuevaZona("Centro");
        var id = await _repo.AgregarAsync(zona);
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
        var zona = NuevaZona("Nombre Original");
        var id = await _repo.AgregarAsync(zona);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
