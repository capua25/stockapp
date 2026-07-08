using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de integración para ReporteStockRepository.ObtenerValorizacionAsync contra PostgreSQL real.
/// </summary>
public class ReporteStockRepositoryValorizacionTests : PostgresRepositoryTestBase
{
    private readonly ReporteStockRepository _repo;

    public ReporteStockRepositoryValorizacionTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ReporteStockRepository(Context);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static UnidadMedida NuevaUm() => new() { Nombre = "Unidad", Abreviatura = "u" };

    private static Producto NuevoProducto(
        string codigo,
        string nombre,
        UnidadMedida um,
        decimal stockActual,
        decimal precioCosto,
        decimal precioVenta,
        bool activo = true,
        Categoria? categoria = null) => new()
    {
        Codigo      = codigo,
        Nombre      = nombre,
        UnidadMedida = um,
        Categoria   = categoria,
        PrecioCosto = precioCosto,
        PrecioVenta = precioVenta,
        StockActual = stockActual,
        Activo      = activo,
        FechaAlta   = DateTime.UtcNow
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerValorizacionAsync_RetornaProductosActivos()
    {
        var um = NuevaUm();
        var cat = new Categoria { Nombre = "Bebidas", Activo = true };
        Context.UnidadesMedida.Add(um);
        Context.Categorias.Add(cat);
        await Context.SaveChangesAsync();

        // Insertar en orden INVERSO al alfabético (Jugo primero, Agua después)
        // para que el Id de inserción sea distinto al orden esperado por Nombre.
        // Esto fuerza que el OrderBy(p => p.Nombre) sea verificable.
        var activo2   = NuevoProducto("ACT002", "Jugo",   um, stockActual: 4m,  precioCosto: 3m,  precioVenta: 7m,  categoria: cat);
        var activo1   = NuevoProducto("ACT001", "Agua",   um, stockActual: 10m, precioCosto: 2m,  precioVenta: 5m,  categoria: cat);
        var inactivo  = NuevoProducto("INA001", "Vino",   um, stockActual: 8m,  precioCosto: 10m, precioVenta: 20m, activo: false, categoria: cat);
        Context.Productos.AddRange(activo2, activo1, inactivo);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerValorizacionAsync();

        // Solo los 2 activos
        Assert.Equal(2, resultado.Count);
        Assert.DoesNotContain(resultado, x => x.Codigo == "INA001");

        // Verificar que está ordenado alfabéticamente por Nombre a pesar del orden de inserción inverso
        var nombresResultado = resultado.Select(r => r.Nombre).ToArray();
        Assert.Equal(new[] { "Agua", "Jugo" }, nombresResultado);

        // ValorCosto/ValorVenta = Stock * precio
        var agua = resultado[0];
        Assert.Equal("Agua", agua.Nombre);
        Assert.Equal(10m * 2m, agua.ValorCosto);   // 20
        Assert.Equal(10m * 5m, agua.ValorVenta);   // 50

        var jugo = resultado[1];
        Assert.Equal("Jugo", jugo.Nombre);
        Assert.Equal(4m * 3m, jugo.ValorCosto);    // 12
        Assert.Equal(4m * 7m, jugo.ValorVenta);    // 28
    }

    [Fact]
    public async Task ObtenerValorizacionAsync_ProductoSinCategoria_GrupoSinCategoria()
    {
        var um = NuevaUm();
        Context.UnidadesMedida.Add(um);
        await Context.SaveChangesAsync();

        // CategoriaId null → "Sin categoría"
        var sinCat = NuevoProducto("SC001", "Genérico", um, stockActual: 5m, precioCosto: 1m, precioVenta: 2m, categoria: null);
        Context.Productos.Add(sinCat);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerValorizacionAsync();

        Assert.Single(resultado);
        Assert.Equal("Sin categoría", resultado[0].Categoria);
    }

    [Fact]
    public async Task ObtenerValorizacionAsync_TotalesCorrectos()
    {
        var um = NuevaUm();
        var cat = new Categoria { Nombre = "Limpieza", Activo = true };
        Context.UnidadesMedida.Add(um);
        Context.Categorias.Add(cat);
        await Context.SaveChangesAsync();

        var p1 = NuevoProducto("T001", "Lavandina", um, stockActual: 3m, precioCosto: 4m, precioVenta: 9m,  categoria: cat);
        var p2 = NuevoProducto("T002", "Esponja",   um, stockActual: 6m, precioCosto: 1m, precioVenta: 3m,  categoria: cat);
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerValorizacionAsync();

        // Per-item: la suma de estos permite que el service arme el total
        var totalCosto = resultado.Sum(x => x.ValorCosto);
        var totalVenta = resultado.Sum(x => x.ValorVenta);

        // p1: 3*4=12 costo, 3*9=27 venta ; p2: 6*1=6 costo, 6*3=18 venta
        Assert.Equal(12m + 6m,  totalCosto);   // 18
        Assert.Equal(27m + 18m, totalVenta);   // 45

        // Confirmar valores per-item explícitos
        var lavandina = resultado.Single(x => x.Codigo == "T001");
        Assert.Equal(12m, lavandina.ValorCosto);
        Assert.Equal(27m, lavandina.ValorVenta);
    }
}
