using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    // ── Normalización case-insensitive (LOWER) ───────────────────────────────

    [Fact]
    public async Task ExisteNombreAsync_DiferenteCasing_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaCategoria("Bebidas"));

        Assert.True(await _repo.ExisteNombreAsync("bebidas"));
        Assert.True(await _repo.ExisteNombreAsync("BEBIDAS"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ConEspaciosYCasingDistinto_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaCategoria("Bebidas"));

        Assert.True(await _repo.ExisteNombreAsync("  bebidas  "));
    }

    [Fact]
    public async Task ExisteNombreAsync_TraduceToLowerAServidor_NoEvaluaEnCliente()
    {
        // Evidencia de que .ToLower() se traduce a SQL server-side (lower(...)), no se evalúa
        // en cliente: se abre un contexto separado con logging de EF Core habilitado y se
        // inspecciona el comando SQL real ejecutado contra Postgres.
        await _repo.AgregarAsync(NuevaCategoria("Bebidas"));
        Context.ChangeTracker.Clear();

        var logs = new List<string>();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .LogTo(logs.Add, LogLevel.Debug)
            .EnableSensitiveDataLogging()
            .Options;

        await using var loggedContext = new AppDbContext(options);
        var repoConLogging = new CategoriaRepository(loggedContext);

        var existe = await repoConLogging.ExisteNombreAsync("bebidas");

        Assert.True(existe);
        var comandoSql = logs.FirstOrDefault(l =>
            l.Contains("SELECT", StringComparison.Ordinal) && l.Contains("\"Categorias\"", StringComparison.Ordinal));
        Assert.NotNull(comandoSql);
        Assert.Contains("lower(", comandoSql!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndiceDeBase_RechazaDuplicadoPorCasing_AunSinPasarPorElRepositorio()
    {
        // Guardián real contra la condición de carrera: inserta por fuera de ExisteNombreAsync
        // (directo por EF) y confirma que es el ÍNDICE FUNCIONAL de la base el que rechaza,
        // no solo la validación de C#.
        Context.Categorias.Add(new Categoria { Nombre = "Bebidas" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.Categorias.Add(new Categoria { Nombre = "bebidas" });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
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
