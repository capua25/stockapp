using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests para MovimientoStockRepository usando SQLite in-memory.
/// Patrón: conexión abierta explícita + EnsureCreated (igual que ProductoRepositoryTests).
/// </summary>
public class MovimientoStockRepositoryTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly AppDbContext _ctx;
    private readonly MovimientoStockRepository _repo;

    public MovimientoStockRepositoryTests()
    {
        // Conexión nombrada: permite abrir un segundo context sobre la MISMA BD in-memory (crítico para C4).
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=movimientos_test;Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _ctx = new AppDbContext(options);
        _ctx.Database.EnsureCreated();

        _repo = new MovimientoStockRepository(_ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static UnidadMedida NuevaUm() => new() { Nombre = "Unidad", Abreviatura = "u" };

    private static Usuario NuevoUsuario(string nombre = "admin") => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "hash",
        Rol            = RolUsuario.Admin,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow
    };

    private static Producto NuevoProducto(string codigo, UnidadMedida um, decimal stockActual = 0m) => new()
    {
        Codigo      = codigo,
        Nombre      = $"Producto {codigo}",
        UnidadMedida = um,
        PrecioCosto = 10m,
        PrecioVenta = 20m,
        StockActual = stockActual,
        Activo      = true,
        FechaAlta   = DateTime.UtcNow
    };

    private async Task<(UnidadMedida um, Usuario usuario, Producto producto)> SeedBaseAsync(
        decimal stockInicial = 100m)
    {
        var um      = NuevaUm();
        var usuario = NuevoUsuario();
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var producto = NuevoProducto("SKU001", um, stockInicial);
        _ctx.Productos.Add(producto);
        await _ctx.SaveChangesAsync();

        return (um, usuario, producto);
    }

    // ── C1: ObtenerProductoAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ObtenerProductoAsync_ProductoExistente_Devuelve()
    {
        var (_, _, producto) = await SeedBaseAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerProductoAsync(producto.Id);

        Assert.NotNull(resultado);
        Assert.Equal(producto.Id,     resultado!.Id);
        Assert.Equal("SKU001",        resultado.Codigo);
        Assert.Equal(100m,            resultado.StockActual);
    }

    [Fact]
    public async Task ObtenerProductoAsync_ProductoInexistente_RetornaNull()
    {
        var resultado = await _repo.ObtenerProductoAsync(99999);

        Assert.Null(resultado);
    }

    // ── C2: SumarMovimientosAsync ─────────────────────────────────────────────

    [Fact]
    public async Task SumarMovimientosAsync_MovimientosMixtos_DevuelveNetoYTotal()
    {
        var (_, usuario, producto) = await SeedBaseAsync();

        // 10 entrada + 5 entrada - 3 salida = neto 12, total 3
        _ctx.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,  Cantidad = 3m,  PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta  }
        );
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var (neto, total) = await _repo.SumarMovimientosAsync(producto.Id);

        Assert.Equal(12m, neto);
        Assert.Equal(3,   total);
    }

    [Fact]
    public async Task SumarMovimientosAsync_SinMovimientos_DevuelveCeroYTotal0()
    {
        var (_, _, producto) = await SeedBaseAsync();
        _ctx.ChangeTracker.Clear();

        var (neto, total) = await _repo.SumarMovimientosAsync(producto.Id);

        Assert.Equal(0m, neto);
        Assert.Equal(0,  total);
    }

    // ── C3: RegistrarMovimientoAtomicoAsync (éxito — single SaveChanges) ──────

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_DatosValidos_PersisteTresRegistros()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 50m);
        int productoId = producto.Id;
        int usuarioId  = usuario.Id;

        var movimiento = new MovimientoStock
        {
            ProductoId    = productoId,
            UsuarioId     = usuarioId,
            Tipo          = TipoMovimiento.Entrada,
            Cantidad      = 20m,
            PrecioUnitario = 5m,
            Fecha         = DateTime.UtcNow,
            Motivo        = MotivoMovimiento.Compra
        };

        var args = new RegistroAtomicoArgs(
            Movimiento:       movimiento,
            ProductoId:       productoId,
            StockNuevo:       70m,        // 50 + 20
            UsuarioId:        usuarioId,
            DetalleAuditoria: "ProductoId=1; Tipo=Entrada; Cantidad=20; StockAnterior=50; StockNuevo=70"
        );

        var movId = await _repo.RegistrarMovimientoAtomicoAsync(args);

        // Verificar con context FRESCO sobre la misma conexión
        var opts2 = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var ctx2 = new AppDbContext(opts2);

        Assert.True(movId > 0, "Debe retornar el Id generado del movimiento");
        Assert.Equal(1, await ctx2.MovimientosStock.CountAsync());

        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(70m, productoFresh!.StockActual);

        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
        var log = await ctx2.LogsAuditoria.FirstAsync();
        Assert.Equal((int)AccionAuditada.RegistroMovimiento, (int)log.Accion);
        Assert.Equal(17, (int)log.Accion);
    }
}
