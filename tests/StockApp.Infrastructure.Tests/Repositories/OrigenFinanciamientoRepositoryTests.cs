using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class OrigenFinanciamientoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly OrigenFinanciamientoRepository _repo;

    public OrigenFinanciamientoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new OrigenFinanciamientoRepository(Context);
    }

    private static OrigenFinanciamiento NuevoOrigen(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var origen = NuevoOrigen("Presupuesto propio");
        var id = await _repo.AgregarAsync(origen);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Presupuesto propio", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevoOrigen("Presupuesto propio", activo: true));
        await _repo.AgregarAsync(NuevoOrigen("Fondo nacional", activo: true));
        await _repo.AgregarAsync(NuevoOrigen("Convenio discontinuado", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevoOrigen("Fondo nacional"));
        await _repo.AgregarAsync(NuevoOrigen("Convenio"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Convenio", result[0].Nombre);
        Assert.Equal("Fondo nacional", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoOrigen("Presupuesto propio"));

        Assert.True(await _repo.ExisteNombreAsync("Presupuesto propio"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismoOrigen_RetornaFalse()
    {
        var origen = NuevoOrigen("Presupuesto propio");
        var id = await _repo.AgregarAsync(origen);

        Assert.False(await _repo.ExisteNombreAsync("Presupuesto propio", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtroTieneMismoNombre_RetornaTrue()
    {
        var origen1 = NuevoOrigen("Presupuesto propio");
        var origen2 = NuevoOrigen("Fondo nacional");
        await _repo.AgregarAsync(origen1);
        var id2 = await _repo.AgregarAsync(origen2);

        Assert.True(await _repo.ExisteNombreAsync("Presupuesto propio", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var origen = NuevoOrigen("Presupuesto propio");
        var id = await _repo.AgregarAsync(origen);
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
        var origen = NuevoOrigen("Nombre Original");
        var id = await _repo.AgregarAsync(origen);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
