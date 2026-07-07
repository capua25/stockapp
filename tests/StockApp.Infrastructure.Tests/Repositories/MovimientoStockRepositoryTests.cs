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

    // ── C5: RecalcularAtomicoAsync ────────────────────────────────────────────

    [Fact]
    public async Task RecalcularAtomicoAsync_ProductoExiste_ActualizaStockYAuditaConAccion18()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 50m);
        int productoId = producto.Id;
        int usuarioId  = usuario.Id;

        var args = new RecalculoAtomicoArgs(
            ProductoId:       productoId,
            StockNuevo:       30m,   // recalculado por el service
            UsuarioId:        usuarioId,
            DetalleAuditoria: "Recálculo; StockAnterior=50; StockNuevo=30; Total=3"
        );

        await _repo.RecalcularAtomicoAsync(args);

        // Verificar con context fresco
        var opts2 = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        await using var ctx2 = new AppDbContext(opts2);

        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(30m, productoFresh!.StockActual);

        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
        var log = await ctx2.LogsAuditoria.FirstAsync();
        Assert.Equal(18, (int)log.Accion);   // AccionAuditada.RecalculoStock
    }

    // ── C6: ObtenerHistorialAsync con filtros combinables ─────────────────────

    /// Helper para seed de movimientos múltiples con fechas configurables.
    private async Task<(Producto p1, Producto p2, Usuario u)> SeedHistorialAsync()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario("hist_user");
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var p1 = NuevoProducto("HIST001", um, 100m);
        p1.Nombre = "Producto Alfa";
        var p2 = NuevoProducto("HIST002", um, 200m);
        p2.Nombre = "Producto Beta";
        _ctx.Productos.AddRange(p1, p2);
        await _ctx.SaveChangesAsync();

        var base1 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        _ctx.MovimientosStock.AddRange(
            // p1: 2 entradas, 1 salida
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = base1,               Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,  Cantidad = 3m,  PrecioUnitario = 8m, Fecha = base1.AddDays(1),    Motivo = MotivoMovimiento.Venta  },
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = base1.AddDays(2),    Motivo = MotivoMovimiento.Compra },
            // p2: 1 entrada
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 20m, PrecioUnitario = 3m, Fecha = base1.AddDays(3),    Motivo = MotivoMovimiento.Compra }
        );
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        return (p1, p2, usuario);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_SinFiltros_DevuelveTodosOrdenadosDesc()
    {
        var (_, _, _) = await SeedHistorialAsync();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro());

        Assert.Equal(4, resultado.Count);
        // Ordenados DESC por Fecha → el más reciente primero
        for (int i = 0; i < resultado.Count - 1; i++)
            Assert.True(resultado[i].Fecha >= resultado[i + 1].Fecha);
    }

    /// <summary>
    /// BUG-02: el orden final debe ser por Fecha DESC GLOBAL, sin importar ProductoId.
    /// Repro: p1 tiene ID bajo pero un movimiento de fecha RECIENTE (hoy); p2 tiene ID alto
    /// pero un movimiento de fecha VIEJA (hace 30 días). El cálculo del running balance
    /// necesita recorrer ASC por ProductoId+Fecha, pero el `.Reverse()` sobre esa lista deja
    /// el resultado ordenado por ProductoId DESC como clave primaria — entierra el movimiento
    /// reciente de p1 debajo del movimiento viejo de p2. El test existente
    /// `ObtenerHistorialAsync_SinFiltros_DevuelveTodosOrdenadosDesc` no detecta esto porque
    /// su fixture (SeedHistorialAsync) alinea ID y fecha en la misma dirección.
    /// </summary>
    [Fact]
    public async Task ObtenerHistorialAsync_OrdenPorFechaDesc_NoDependeDelProductoId()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario("orden_user");
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var p1 = NuevoProducto("ORD001", um, 0m); // ID bajo (creado primero)
        p1.Nombre = "Producto Reciente";
        var p2 = NuevoProducto("ORD002", um, 0m); // ID alto (creado después)
        p2.Nombre = "Producto Viejo";
        _ctx.Productos.AddRange(p1, p2);
        await _ctx.SaveChangesAsync();

        var fechaVieja     = DateTime.UtcNow.AddDays(-30);
        var fechaReciente  = DateTime.UtcNow;

        _ctx.MovimientosStock.AddRange(
            // p2 (ID alto): movimiento viejo
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = fechaVieja,    Motivo = MotivoMovimiento.Compra },
            // p1 (ID bajo): movimiento reciente
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = fechaReciente, Motivo = MotivoMovimiento.Compra }
        );
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro());

        Assert.Equal(2, resultado.Count);
        // El primer item debe ser el movimiento MÁS RECIENTE (p1), pese a tener ProductoId menor.
        Assert.Equal(p1.Id, resultado[0].ProductoId);
        Assert.Equal(p2.Id, resultado[1].ProductoId);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_FiltroPorProducto_DevuelveSoloDelProducto()
    {
        var (p1, _, _) = await SeedHistorialAsync();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(ProductoId: p1.Id));

        Assert.Equal(3, resultado.Count);
        Assert.All(resultado, m => Assert.Equal(p1.Id, m.ProductoId));
        Assert.All(resultado, m => Assert.Equal("Producto Alfa", m.ProductoNombre));
    }

    [Fact]
    public async Task ObtenerHistorialAsync_FiltroPorTipo_DevuelveSoloTipoIndicado()
    {
        var (_, _, _) = await SeedHistorialAsync();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(Tipo: TipoMovimiento.Salida));

        Assert.Single(resultado);
        Assert.Equal(TipoMovimiento.Salida, resultado[0].Tipo);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_FiltroPorPeriodo_AplicaFechaDesdeHasta()
    {
        var (_, _, _) = await SeedHistorialAsync();
        var base1 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        // FechaDesde=día 10, FechaHasta=día 11 → sólo los 2 primeros movimientos
        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(
            FechaDesde: base1,
            FechaHasta: base1.AddDays(1)));

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_FiltrosCombinados_AplicaAnd()
    {
        var (p1, _, _) = await SeedHistorialAsync();
        var base1 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        // p1 + Tipo=Entrada + FechaDesde=día10 → sólo el primer movimiento de p1
        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(
            ProductoId: p1.Id,
            Tipo:       TipoMovimiento.Entrada,
            FechaDesde: base1,
            FechaHasta: base1.AddDays(1)));

        Assert.Single(resultado);
        Assert.Equal(p1.Id,               resultado[0].ProductoId);
        Assert.Equal(TipoMovimiento.Entrada, resultado[0].Tipo);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_SinCoincidencias_DevuelveVacio()
    {
        var (_, _, _) = await SeedHistorialAsync();

        // Fecha en el pasado lejano → sin coincidencias
        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(
            FechaDesde: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FechaHasta: new DateTime(2020, 12, 31, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Empty(resultado);
    }

    /// <summary>
    /// HM-04: FechaHasta sin componente de hora (medianoche) debe tratarse como fin de día.
    /// Un movimiento registrado a las 15:00 del mismo día de FechaHasta debe ser incluido.
    /// </summary>
    [Fact]
    public async Task ObtenerHistorialAsync_FechaHastaMedianoche_IncluyeMovimientosDelMismoDia()
    {
        // Seed: producto con un movimiento a las 15:00 del 2026-06-10
        var um      = NuevaUm();
        var usuario = NuevoUsuario("hm04_user");
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var p = NuevoProducto("HM04", um, 0m);
        _ctx.Productos.Add(p);
        await _ctx.SaveChangesAsync();

        var fechaMovimiento = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc);
        _ctx.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId     = p.Id,
            UsuarioId      = usuario.Id,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 5m,
            PrecioUnitario = 10m,
            Fecha          = fechaMovimiento,
            Motivo         = MotivoMovimiento.Compra
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        // FechaHasta = mismo día pero a las 00:00:00 (medianoche) → debe tratarse como fin de día
        var fechaHasta = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(
            FechaHasta: fechaHasta));

        // HM-04: el movimiento de las 15:00 SÍ debe aparecer (FechaHasta = fin del día)
        Assert.Single(resultado);
        Assert.Equal(p.Id, resultado[0].ProductoId);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_RunningBalance_StockAnteriorYNuevoCorrectos()
    {
        // Seed de 1 producto con 2 entradas para verificar running balance
        var um      = NuevaUm();
        var usuario = NuevoUsuario("rb_user");
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var p = NuevoProducto("RB001", um, 0m);
        _ctx.Productos.Add(p);
        await _ctx.SaveChangesAsync();

        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        _ctx.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = t0,            Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = p.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = t0.AddHours(1), Motivo = MotivoMovimiento.Compra }
        );
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(ProductoId: p.Id));

        // Resultado ordenado DESC: mov2 (t0+1h) primero, mov1 (t0) segundo
        Assert.Equal(2, resultado.Count);

        // mov más reciente (5 entrada): StockAnterior=10, StockNuevo=15
        Assert.Equal(10m, resultado[0].StockAnterior);
        Assert.Equal(15m, resultado[0].StockNuevo);

        // mov más antiguo (10 entrada): StockAnterior=0, StockNuevo=10
        Assert.Equal(0m,  resultado[1].StockAnterior);
        Assert.Equal(10m, resultado[1].StockNuevo);
    }

    // ── UsuarioNombre en el historial ─────────────────────────────────────────

    [Fact]
    public async Task ObtenerHistorialAsync_PopulaUsuarioNombre_ConNombreCompleto()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario("jperez");
        usuario.NombreCompleto = "Juan Pérez";
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var p = NuevoProducto("USR001", um, 0m);
        _ctx.Productos.Add(p);
        await _ctx.SaveChangesAsync();

        _ctx.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId     = p.Id,
            UsuarioId      = usuario.Id,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 5m,
            PrecioUnitario = 10m,
            Fecha          = DateTime.UtcNow,
            Motivo         = MotivoMovimiento.Compra
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(ProductoId: p.Id));

        Assert.Single(resultado);
        Assert.Equal("Juan Pérez", resultado[0].UsuarioNombre);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_PopulaUsuarioNombre_FallbackANombreUsuarioSiNoHayNombreCompleto()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario("mgomez"); // NombreCompleto queda null (default del helper)
        _ctx.UnidadesMedida.Add(um);
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();

        var p = NuevoProducto("USR002", um, 0m);
        _ctx.Productos.Add(p);
        await _ctx.SaveChangesAsync();

        _ctx.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId     = p.Id,
            UsuarioId      = usuario.Id,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 5m,
            PrecioUnitario = 10m,
            Fecha          = DateTime.UtcNow,
            Motivo         = MotivoMovimiento.Compra
        });
        await _ctx.SaveChangesAsync();
        _ctx.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(ProductoId: p.Id));

        Assert.Single(resultado);
        Assert.Equal("mgomez", resultado[0].UsuarioNombre);
    }
}

/// <summary>
/// Variante de MovimientoStockRepository que inyecta Detalle=null en el LogAuditoria.
/// Usada EXCLUSIVAMENTE en el test de rollback (C4) para forzar DbUpdateException
/// sin tocar la implementación real del repositorio.
/// El método base es virtual → este override es verdadero (no method hiding).
/// Así la verificación del rollback es válida aunque el objeto se tipifique
/// por la clase base o por la interfaz IMovimientoStockRepository (WARNING-02 resuelto).
/// </summary>
internal sealed class MovimientoStockRepositoryConDetalleNulo : MovimientoStockRepository
{
    private readonly AppDbContext _ctx;

    public MovimientoStockRepositoryConDetalleNulo(AppDbContext ctx) : base(ctx)
        => _ctx = ctx;

    public override async Task<int> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
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
