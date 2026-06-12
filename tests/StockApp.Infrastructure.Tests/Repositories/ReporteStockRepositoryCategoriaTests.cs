using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de integración para ReporteStockRepository.ObtenerStockPorCategoriaAsync sobre SQLite in-memory.
/// </summary>
public class ReporteStockRepositoryCategoriaTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly AppDbContext _ctx;
    private readonly ReporteStockRepository _repo;

    public ReporteStockRepositoryCategoriaTests()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection(
            "DataSource=reporte_categoria_test;Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _ctx = new AppDbContext(options);
        _ctx.Database.EnsureCreated();

        _repo = new ReporteStockRepository(_ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Close();
        _connection.Dispose();
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
    public async Task ObtenerStockPorCategoriaAsync_AgrupaCorrectamente()
    {
        var um = NuevaUm();
        var bebidas  = new Categoria { Nombre = "Bebidas",  Activo = true };
        var limpieza = new Categoria { Nombre = "Limpieza", Activo = true };
        _ctx.UnidadesMedida.Add(um);
        _ctx.Categorias.AddRange(bebidas, limpieza);
        await _ctx.SaveChangesAsync();

        // Bebidas: 2 productos
        var b1 = NuevoProducto("B001", "Agua", um, stockActual: 10m, precioCosto: 2m, precioVenta: 5m, categoria: bebidas);
        var b2 = NuevoProducto("B002", "Jugo", um, stockActual: 4m,  precioCosto: 3m, precioVenta: 7m, categoria: bebidas);
        // Limpieza: 1 producto
        var l1 = NuevoProducto("L001", "Lavandina", um, stockActual: 6m, precioCosto: 1m, precioVenta: 3m, categoria: limpieza);
        _ctx.Productos.AddRange(b1, b2, l1);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerStockPorCategoriaAsync();

        Assert.Equal(2, resultado.Count);

        var gBebidas = resultado.Single(x => x.Categoria == "Bebidas");
        Assert.Equal(2, gBebidas.CantidadProductos);
        Assert.Equal(10m + 4m, gBebidas.StockTotal);                       // 14
        Assert.Equal(10m * 2m + 4m * 3m, gBebidas.ValorCosto);            // 20 + 12 = 32
        Assert.Equal(10m * 5m + 4m * 7m, gBebidas.ValorVenta);           // 50 + 28 = 78

        var gLimpieza = resultado.Single(x => x.Categoria == "Limpieza");
        Assert.Equal(1, gLimpieza.CantidadProductos);
        Assert.Equal(6m, gLimpieza.StockTotal);
        Assert.Equal(6m * 1m, gLimpieza.ValorCosto);                      // 6
        Assert.Equal(6m * 3m, gLimpieza.ValorVenta);                      // 18
    }

    [Fact]
    public async Task ObtenerStockPorCategoriaAsync_GrupoSinCategoria_Presente()
    {
        var um = NuevaUm();
        _ctx.UnidadesMedida.Add(um);
        await _ctx.SaveChangesAsync();

        // 2 productos sin categoría
        var s1 = NuevoProducto("S001", "Genérico A", um, stockActual: 3m, precioCosto: 2m, precioVenta: 4m, categoria: null);
        var s2 = NuevoProducto("S002", "Genérico B", um, stockActual: 5m, precioCosto: 1m, precioVenta: 2m, categoria: null);
        _ctx.Productos.AddRange(s1, s2);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerStockPorCategoriaAsync();

        var sinCat = resultado.Single(x => x.Categoria == "Sin categoría");
        Assert.Equal(2, sinCat.CantidadProductos);
        Assert.Equal(3m + 5m, sinCat.StockTotal);                         // 8
        Assert.Equal(3m * 2m + 5m * 1m, sinCat.ValorCosto);              // 6 + 5 = 11
        Assert.Equal(3m * 4m + 5m * 2m, sinCat.ValorVenta);             // 12 + 10 = 22
    }

    [Fact]
    public async Task ObtenerStockPorCategoriaAsync_CategoriasSinProductosActivos_NoAparecen()
    {
        var um = NuevaUm();
        var conActivos = new Categoria { Nombre = "ConActivos", Activo = true };
        var soloInactivos = new Categoria { Nombre = "SoloInactivos", Activo = true };
        _ctx.UnidadesMedida.Add(um);
        _ctx.Categorias.AddRange(conActivos, soloInactivos);
        await _ctx.SaveChangesAsync();

        var activo = NuevoProducto("A001", "Producto Activo", um, stockActual: 5m, precioCosto: 2m, precioVenta: 4m, categoria: conActivos);
        // Todos los de soloInactivos están inactivos → la categoría NO debe aparecer
        var inactivo = NuevoProducto("I001", "Producto Inactivo", um, stockActual: 9m, precioCosto: 3m, precioVenta: 6m, activo: false, categoria: soloInactivos);
        _ctx.Productos.AddRange(activo, inactivo);
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerStockPorCategoriaAsync();

        Assert.Single(resultado);
        Assert.Equal("ConActivos", resultado[0].Categoria);
        Assert.DoesNotContain(resultado, x => x.Categoria == "SoloInactivos");
    }
}
