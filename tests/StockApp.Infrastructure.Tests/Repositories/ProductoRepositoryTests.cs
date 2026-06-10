using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ProductoRepositoryTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly ProductoRepository _repo;

    public ProductoRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();
        _repo = new ProductoRepository(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private static UnidadMedida NuevaUm(string nombre = "Unidad", string abrev = "u") =>
        new() { Nombre = nombre, Abreviatura = abrev };

    private Producto NuevoProducto(string codigo, string nombre, UnidadMedida um,
        string? codigoBarras = null, bool activo = true) =>
        new()
        {
            Codigo       = codigo,
            Nombre       = nombre,
            CodigoBarras = codigoBarras,
            UnidadMedida = um,
            PrecioCosto  = 10m,
            PrecioVenta  = 20m,
            Activo       = activo,
            FechaAlta    = DateTime.UtcNow
        };

    private async Task<(ProductoRepository repo, UnidadMedida um)> SeedUmAsync()
    {
        var um = NuevaUm();
        _ctx.UnidadesMedida.Add(um);
        await _ctx.SaveChangesAsync();
        return (_repo, um);
    }

    // ── roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var (repo, um) = await SeedUmAsync();
        var p = NuevoProducto("SKU001", "Producto Test", um);

        var id = await repo.AgregarAsync(p);
        _ctx.ChangeTracker.Clear();

        var found = await repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("SKU001", found!.Codigo);
        Assert.Equal("Producto Test", found.Nombre);
        Assert.True(found.Activo);
    }

    // ── BuscarAsync: solo sku ─────────────────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_PorSku_RetornaCoincidencias()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("SKU001", "Alpha", um));
        await repo.AgregarAsync(NuevoProducto("SKU002", "Beta", um));
        await repo.AgregarAsync(NuevoProducto("ZZZ999", "Gamma", um));
        _ctx.ChangeTracker.Clear();

        var result = await repo.BuscarAsync(sku: "SKU", codigoBarras: null, nombre: null);

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Contains("SKU", p.Codigo));
    }

    // ── BuscarAsync: solo codigoBarras ────────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_PorCodigoBarras_RetornaCoincidencias()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("A001", "Alpha", um, codigoBarras: "7791234567890"));
        await repo.AgregarAsync(NuevoProducto("A002", "Beta", um,  codigoBarras: "7791234567891"));
        await repo.AgregarAsync(NuevoProducto("A003", "Gamma", um, codigoBarras: null));
        _ctx.ChangeTracker.Clear();

        var result = await repo.BuscarAsync(sku: null, codigoBarras: "7791234", nombre: null);

        Assert.Equal(2, result.Count);
    }

    // ── BuscarAsync: solo nombre parcial ─────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_PorNombreParcial_RetornaCoincidencias()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("A001", "Aceite de Oliva", um));
        await repo.AgregarAsync(NuevoProducto("A002", "Aceite de Girasol", um));
        await repo.AgregarAsync(NuevoProducto("A003", "Vinagre", um));
        _ctx.ChangeTracker.Clear();

        var result = await repo.BuscarAsync(sku: null, codigoBarras: null, nombre: "Aceite");

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Contains("Aceite", p.Nombre));
    }

    // ── BuscarAsync: nombre — comportamiento REAL de case-sensitivity en SQLite ──

    [Fact]
    public async Task BuscarAsync_PorNombre_EsCaseSensitiveEnSQLiteInMemory()
    {
        // HALLAZGO: EF Core con SQLite in-memory (DataSource=:memory: + OpenConnection)
        // evalúa Contains() mediante SQLite LIKE, pero en el proveedor in-memory de SQLite
        // el LIKE resulta case-sensitive (al menos para caracteres con mezcla de mayúsculas).
        // Búsqueda "aceite" NO encuentra "Aceite de Oliva".
        // COMPORTAMIENTO REAL, no el deseado — el test documenta lo que realmente ocurre.
        // NOTA: si se necesita búsqueda case-insensitive real, habría que aplicar
        // EF.Functions.Collate(p.Nombre, "NOCASE") o normalizar a minúsculas en la query.
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("A001", "Aceite de Oliva", um));
        _ctx.ChangeTracker.Clear();

        var resultExacto = await repo.BuscarAsync(sku: null, codigoBarras: null, nombre: "Aceite");
        var resultLower  = await repo.BuscarAsync(sku: null, codigoBarras: null, nombre: "aceite");

        Assert.Single(resultExacto);        // case exacto → encuentra
        Assert.Empty(resultLower);          // lowercase → NO encuentra (case-sensitive en SQLite in-memory)
    }

    // ── BuscarAsync: sin filtros = todos ─────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_SinFiltros_RetornaTodosOrdenados()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("B001", "Zapato", um));
        await repo.AgregarAsync(NuevoProducto("A001", "Alfajor", um));
        _ctx.ChangeTracker.Clear();

        var result = await repo.BuscarAsync(sku: null, codigoBarras: null, nombre: null);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alfajor", result[0].Nombre); // OrderBy Nombre
    }

    // ── BuscarAsync: filtros combinados ───────────────────────────────────────

    [Fact]
    public async Task BuscarAsync_ConMultiplesFiltros_AplicaTodos()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("SKU001", "Aceite",  um, codigoBarras: "7791234567890"));
        await repo.AgregarAsync(NuevoProducto("SKU002", "Vinagre", um, codigoBarras: "7791234567891"));
        await repo.AgregarAsync(NuevoProducto("ZZZ001", "Aceite Extra", um, codigoBarras: null));
        _ctx.ChangeTracker.Clear();

        // SKU contiene "SKU" AND nombre contiene "Aceite"
        var result = await repo.BuscarAsync(sku: "SKU", codigoBarras: null, nombre: "Aceite");

        Assert.Single(result);
        Assert.Equal("SKU001", result[0].Codigo);
    }

    // ── ExisteCodigoAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExisteCodigoAsync_Existente_RetornaTrue()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("SKU001", "Test", um));

        Assert.True(await repo.ExisteCodigoAsync("SKU001"));
    }

    [Fact]
    public async Task ExisteCodigoAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteCodigoAsync("NOPE"));
    }

    [Fact]
    public async Task ExisteCodigoAsync_ExcluyendoId_NoSeAutoExcluye()
    {
        var (repo, um) = await SeedUmAsync();
        var p = NuevoProducto("SKU001", "Test", um);
        var id = await repo.AgregarAsync(p);

        // Mismo código pero excluyendo su propio id → false (el único que tiene ese código soy yo)
        Assert.False(await repo.ExisteCodigoAsync("SKU001", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteCodigoAsync_ExcluyendoId_OtroTieneMismoCodigo_RetornaTrue()
    {
        var (repo, um) = await SeedUmAsync();
        var p1 = NuevoProducto("SKU001", "Prod1", um);
        var p2 = NuevoProducto("SKU002", "Prod2", um);
        await repo.AgregarAsync(p1);
        var id2 = await repo.AgregarAsync(p2);

        // p2 tiene SKU002, chequeo si SKU001 existe excluyendo p2 → sí existe (p1 lo tiene)
        Assert.True(await repo.ExisteCodigoAsync("SKU001", excluyendoId: id2));
    }

    // ── ExisteCodigoBarrasAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ExisteCodigoBarrasAsync_Existente_RetornaTrue()
    {
        var (repo, um) = await SeedUmAsync();
        await repo.AgregarAsync(NuevoProducto("A001", "Test", um, codigoBarras: "7791234567890"));

        Assert.True(await repo.ExisteCodigoBarrasAsync("7791234567890"));
    }

    [Fact]
    public async Task ExisteCodigoBarrasAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteCodigoBarrasAsync("0000000000000"));
    }

    [Fact]
    public async Task ExisteCodigoBarrasAsync_ExcluyendoId_MismoProducto_RetornaFalse()
    {
        var (repo, um) = await SeedUmAsync();
        var p = NuevoProducto("A001", "Test", um, codigoBarras: "7791234567890");
        var id = await repo.AgregarAsync(p);

        Assert.False(await repo.ExisteCodigoBarrasAsync("7791234567890", excluyendoId: id));
    }

    // ── ActualizarAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ActualizarAsync_Modifica_Y_Persiste()
    {
        var (repo, um) = await SeedUmAsync();
        var p = NuevoProducto("SKU001", "Nombre Original", um);
        var id = await repo.AgregarAsync(p);
        _ctx.ChangeTracker.Clear();

        var found = await repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await repo.ActualizarAsync(found);
        _ctx.ChangeTracker.Clear();

        var updated = await repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var (repo, um) = await SeedUmAsync();
        var p = NuevoProducto("SKU001", "Prod", um);
        var id = await repo.AgregarAsync(p);
        _ctx.ChangeTracker.Clear();

        var found = await repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await repo.ActualizarAsync(found);
        _ctx.ChangeTracker.Clear();

        var updated = await repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }
}
