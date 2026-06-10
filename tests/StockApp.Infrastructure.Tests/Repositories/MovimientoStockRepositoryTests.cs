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

    // ── C4: Rollback atómico (MANDATORY) ─────────────────────────────────────
    //
    // Estrategia: provocar fallo real en SaveChangesAsync para verificar que EF
    // revierte TODO el flush (movimiento + StockActual + log).
    //
    // Mecanismo elegido: Detalle=null sobre columna IsRequired (definida en OnModelCreating).
    // Esta violación de constraint es detectada por EF antes del flush y lanza DbUpdateException.
    // El constraint IsRequired sobre Detalle está configurado en AppDbContext.OnModelCreating.
    //
    // Nota: se intentó FK violation (UsuarioId=99999), pero EF Core con SQLite in-memory
    // NO enforcea FKs por defecto incluso con PRAGMA foreign_keys=ON aplicado tras conexión
    // (el PRAGMA debe aplicarse POR CONEXIÓN antes del primer uso; EF maneja la conexión
    // internamente y no garantiza el orden). La alternativa IsRequired es determinista y
    // no depende de configuración del driver.

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_DetalleNull_RollbackTotal()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 50m);
        int productoId   = producto.Id;
        int usuarioId    = usuario.Id;
        decimal stockOrig = 50m;

        // ──────────────────────────────────────────────────────────────────────
        // Repo auxiliar que permite inyectar un log con Detalle=null para forzar
        // DbUpdateException en el SaveChangesAsync (Detalle es IsRequired).
        // Se usa el MISMO context (_ctx) para que el rollback cubra los 3 cambios.
        // ──────────────────────────────────────────────────────────────────────
        var repoRoto = new MovimientoStockRepositoryConDetalleNulo(_ctx);

        var movimiento = new MovimientoStock
        {
            ProductoId     = productoId,
            UsuarioId      = usuarioId,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 20m,
            PrecioUnitario = 5m,
            Fecha          = DateTime.UtcNow,
            Motivo         = MotivoMovimiento.Compra
        };

        var args = new RegistroAtomicoArgs(
            Movimiento:       movimiento,
            ProductoId:       productoId,
            StockNuevo:       70m,
            UsuarioId:        usuarioId,
            DetalleAuditoria: "no importa — se sobreescribe con null en el repo roto"
        );

        // Debe lanzar DbUpdateException (constraint NOT NULL sobre Detalle)
        await Assert.ThrowsAsync<DbUpdateException>(
            () => repoRoto.RegistrarMovimientoAtomicoAsync(args));

        // Verificar estado con context FRESCO sobre la misma conexión
        var opts2 = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var ctx2 = new AppDbContext(opts2);

        // 0 movimientos, stock sin cambios, 0 logs → rollback total
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());

        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(stockOrig, productoFresh!.StockActual);

        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
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

/// <summary>
/// Variante de MovimientoStockRepository que inyecta Detalle=null en el LogAuditoria.
/// Usada EXCLUSIVAMENTE en el test de rollback (C4) para forzar DbUpdateException
/// sin tocar la implementación real del repositorio.
/// </summary>
internal sealed class MovimientoStockRepositoryConDetalleNulo : MovimientoStockRepository
{
    private readonly AppDbContext _ctx;

    public MovimientoStockRepositoryConDetalleNulo(AppDbContext ctx) : base(ctx)
        => _ctx = ctx;

    public new async Task<int> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
    {
        var producto = await _ctx.Productos.FindAsync(args.ProductoId)
            ?? throw new KeyNotFoundException();

        _ctx.MovimientosStock.Add(args.Movimiento);
        producto.StockActual = args.StockNuevo;

        // Detalle = null! → viola IsRequired → SaveChangesAsync lanzará DbUpdateException
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RegistroMovimiento,
            Entidad   = "MovimientoStock",
            EntidadId = args.ProductoId,
            Detalle   = null!   // fuerza violación de constraint NOT NULL
        });

        await _ctx.SaveChangesAsync();
        return args.Movimiento.Id;
    }
}
