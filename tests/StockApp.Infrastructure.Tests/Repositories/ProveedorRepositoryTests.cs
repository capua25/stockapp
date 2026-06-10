using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ProveedorRepositoryTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly ProveedorRepository _repo;

    public ProveedorRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();
        _repo = new ProveedorRepository(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    private static Proveedor NuevoProveedor(string nombre, bool activo = true) =>
        new()
        {
            Nombre    = nombre,
            Telefono  = "011-1234-5678",
            Email     = "contacto@proveedor.com",
            Direccion = "Av. Corrientes 1234",
            Activo    = activo
        };

    // ── roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var proveedor = NuevoProveedor("Distribuidora Norte");
        var id = await _repo.AgregarAsync(proveedor);
        _ctx.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Distribuidora Norte", found!.Nombre);
        Assert.Equal("011-1234-5678", found.Telefono);
        Assert.True(found.Activo);
    }

    // ── ListarTodosAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ListarTodosAsync_RetornaTodosOrdenadosPorNombre()
    {
        await _repo.AgregarAsync(NuevoProveedor("Proveedor Z"));
        await _repo.AgregarAsync(NuevoProveedor("Proveedor A"));
        await _repo.AgregarAsync(NuevoProveedor("Inactivo", activo: false));
        _ctx.ChangeTracker.Clear();

        var result = await _repo.ListarTodosAsync();

        // ListarTodosAsync devuelve todos (activos e inactivos), ordenados por Nombre
        Assert.Equal(3, result.Count);
        Assert.Equal("Inactivo", result[0].Nombre);
        Assert.Equal("Proveedor A", result[1].Nombre);
        Assert.Equal("Proveedor Z", result[2].Nombre);
    }

    // ── ExisteNombreAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoProveedor("Distribuidora Norte"));

        Assert.True(await _repo.ExisteNombreAsync("Distribuidora Norte"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste SA"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismoProveedor_RetornaFalse()
    {
        var prov = NuevoProveedor("Distribuidora Norte");
        var id = await _repo.AgregarAsync(prov);

        Assert.False(await _repo.ExisteNombreAsync("Distribuidora Norte", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtroTieneMismoNombre_RetornaTrue()
    {
        var p1 = NuevoProveedor("Distribuidora Norte");
        var p2 = NuevoProveedor("Distribuidora Sur");
        await _repo.AgregarAsync(p1);
        var id2 = await _repo.AgregarAsync(p2);

        Assert.True(await _repo.ExisteNombreAsync("Distribuidora Norte", excluyendoId: id2));
    }

    // ── ActualizarAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var prov = NuevoProveedor("Distribuidora Norte");
        var id = await _repo.AgregarAsync(prov);
        _ctx.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        _ctx.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaCamposOpcionales_Persiste()
    {
        var prov = NuevoProveedor("Distribuidora Norte");
        var id = await _repo.AgregarAsync(prov);
        _ctx.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Telefono = "099-9999-9999";
        found.Notas = "Proveedor preferido";
        await _repo.ActualizarAsync(found);
        _ctx.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("099-9999-9999", updated!.Telefono);
        Assert.Equal("Proveedor preferido", updated.Notas);
    }
}
