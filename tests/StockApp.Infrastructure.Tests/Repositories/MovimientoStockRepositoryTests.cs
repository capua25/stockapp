using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de MovimientoStockRepository contra PostgreSQL real (Testcontainers).
/// Cada test parte de tablas truncadas (PostgresRepositoryTestBase).
/// </summary>
public class MovimientoStockRepositoryTests : PostgresRepositoryTestBase
{
    private readonly MovimientoStockRepository _repo;

    public MovimientoStockRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new MovimientoStockRepository(Context);
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
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var producto = NuevoProducto("SKU001", um, stockInicial);
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();

        return (um, usuario, producto);
    }

    // ── C1: ObtenerProductoAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ObtenerProductoAsync_ProductoExistente_Devuelve()
    {
        var (_, _, producto) = await SeedBaseAsync();
        Context.ChangeTracker.Clear();

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
        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,  Cantidad = 3m,  PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta  }
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var (neto, total) = await _repo.SumarMovimientosAsync(producto.Id);

        Assert.Equal(12m, neto);
        Assert.Equal(3,   total);
    }

    [Fact]
    public async Task SumarMovimientosAsync_SinMovimientos_DevuelveCeroYTotal0()
    {
        var (_, _, producto) = await SeedBaseAsync();
        Context.ChangeTracker.Clear();

        var (neto, total) = await _repo.SumarMovimientosAsync(producto.Id);

        Assert.Equal(0m, neto);
        Assert.Equal(0,  total);
    }

    // ── C4: Rollback atómico (MANDATORY) ──────────────────────────────────────
    // Fuerza DbUpdateException con Detalle=null (columna NOT NULL) dentro de la
    // transacción explícita; verifica que el UPDATE de stock también se revierte.

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_DetalleNull_RollbackTotal()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 50m);
        int productoId = producto.Id;
        int usuarioId  = usuario.Id;

        var repoRoto = new MovimientoStockRepositoryConDetalleNulo(Context);

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
            Tipo:             TipoMovimiento.Entrada,
            Cantidad:         20m,
            Forzar:           false,
            UsuarioId:        usuarioId,
            DetalleAuditoria: "se sobreescribe con null en el repo roto");

        await Assert.ThrowsAsync<DbUpdateException>(
            () => repoRoto.RegistrarMovimientoAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(50m, productoFresh!.StockActual);   // stock intacto → rollback del UPDATE
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
    }

    // ── C3: RegistrarMovimientoAtomicoAsync (éxito) ───────────────────────────

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
            Tipo:             TipoMovimiento.Entrada,
            Cantidad:         20m,
            Forzar:           false,
            UsuarioId:        usuarioId,
            DetalleAuditoria: "ProductoId=1; Tipo=Entrada; Cantidad=20; StockAnterior=50; StockNuevo=70");

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        await using var ctx2 = Fixture.CrearContexto();

        Assert.Equal(ResultadoRegistroEstado.Ok, resultado.Estado);
        Assert.True(resultado.MovimientoId > 0, "Debe retornar el Id generado del movimiento");
        Assert.Equal(70m, resultado.StockResultante);
        Assert.Equal(1, await ctx2.MovimientosStock.CountAsync());
        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(70m, productoFresh!.StockActual);
        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
        var log = await ctx2.LogsAuditoria.FirstAsync();
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
            DetalleAuditoria: "Recálculo; StockAnterior=50; StockNuevo=30; Total=3");

        await _repo.RecalcularAtomicoAsync(args);

        // Verificar con context fresco
        await using var ctx2 = Fixture.CrearContexto();

        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(30m, productoFresh!.StockActual);

        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
        var log = await ctx2.LogsAuditoria.FirstAsync();
        Assert.Equal(18, (int)log.Accion);   // AccionAuditada.RecalculoStock
    }

    /// <summary>
    /// Guard de carrera (Task 10 fix): si el producto fue borrado físicamente entre el
    /// check del service y la escritura atómica, RecalcularAtomicoAsync debe lanzar
    /// EntidadNoEncontradaException (no KeyNotFoundException del BCL) para que
    /// DomainExceptionHandler lo mapee a 404 en vez de caer al arm genérico 500.
    /// </summary>
    [Fact]
    public async Task RecalcularAtomicoAsync_ProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var args = new RecalculoAtomicoArgs(
            ProductoId:       99999,
            StockNuevo:       30m,
            UsuarioId:        1,
            DetalleAuditoria: "producto borrado entre el check y la escritura");

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _repo.RecalcularAtomicoAsync(args));

        Assert.Equal("Producto 99999 no encontrado.", ex.Message);
    }

    // ── Guard condicional atómico ─────────────────────────────────────────────

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_SalidaSinStock_NoModificaNada()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 5m);

        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,
            Cantidad = 10m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta
        };
        var args = new RegistroAtomicoArgs(
            Movimiento: movimiento, ProductoId: producto.Id, Tipo: TipoMovimiento.Salida,
            Cantidad: 10m, Forzar: false, UsuarioId: usuario.Id, DetalleAuditoria: "salida sin stock");

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        Assert.Equal(ResultadoRegistroEstado.StockInsuficiente, resultado.Estado);
        Assert.Equal(0, resultado.MovimientoId);
        Assert.Equal(5m, resultado.StockResultante);   // stock actual sin tocar

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
        var p = await ctx2.Productos.FindAsync(producto.Id);
        Assert.Equal(5m, p!.StockActual);
    }

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_SalidaForzada_PermiteNegativo()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 5m);

        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,
            Cantidad = 8m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Merma
        };
        var args = new RegistroAtomicoArgs(
            Movimiento: movimiento, ProductoId: producto.Id, Tipo: TipoMovimiento.Salida,
            Cantidad: 8m, Forzar: true, UsuarioId: usuario.Id, DetalleAuditoria: "salida forzada");

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        Assert.Equal(ResultadoRegistroEstado.Ok, resultado.Estado);
        Assert.Equal(-3m, resultado.StockResultante);   // 5 - 8 = -3, permitido con Forzar

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
    }

    /// <summary>
    /// Guard de carrera (Task 10 fix): salida sin forzar sobre un producto inexistente
    /// (0 filas del UPDATE condicional + StockActual no resoluble) debe lanzar
    /// EntidadNoEncontradaException, no KeyNotFoundException del BCL.
    /// </summary>
    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_SalidaProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var movimiento = new MovimientoStock
        {
            ProductoId = 99999, UsuarioId = 1, Tipo = TipoMovimiento.Salida,
            Cantidad = 10m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta
        };
        var args = new RegistroAtomicoArgs(
            Movimiento: movimiento, ProductoId: 99999, Tipo: TipoMovimiento.Salida,
            Cantidad: 10m, Forzar: false, UsuarioId: 1, DetalleAuditoria: "producto inexistente");

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _repo.RegistrarMovimientoAtomicoAsync(args));

        Assert.Equal("Producto 99999 no encontrado.", ex.Message);
    }

    /// <summary>
    /// Guard de carrera (Task 10 fix): entrada (o salida forzada) sobre un producto
    /// inexistente (0 filas del UPDATE incondicional) debe lanzar
    /// EntidadNoEncontradaException, no KeyNotFoundException del BCL.
    /// </summary>
    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_EntradaProductoInexistente_LanzaEntidadNoEncontrada()
    {
        var movimiento = new MovimientoStock
        {
            ProductoId = 99999, UsuarioId = 1, Tipo = TipoMovimiento.Entrada,
            Cantidad = 10m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Compra
        };
        var args = new RegistroAtomicoArgs(
            Movimiento: movimiento, ProductoId: 99999, Tipo: TipoMovimiento.Entrada,
            Cantidad: 10m, Forzar: false, UsuarioId: 1, DetalleAuditoria: "producto inexistente");

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _repo.RegistrarMovimientoAtomicoAsync(args));

        Assert.Equal("Producto 99999 no encontrado.", ex.Message);
    }

    // ── C6: ObtenerHistorialAsync con filtros combinables ─────────────────────

    /// Helper para seed de movimientos múltiples con fechas configurables.
    private async Task<(Producto p1, Producto p2, Usuario u)> SeedHistorialAsync()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario("hist_user");
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p1 = NuevoProducto("HIST001", um, 100m);
        p1.Nombre = "Producto Alfa";
        var p2 = NuevoProducto("HIST002", um, 200m);
        p2.Nombre = "Producto Beta";
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();

        var base1 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        Context.MovimientosStock.AddRange(
            // p1: 2 entradas, 1 salida
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = base1,               Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,  Cantidad = 3m,  PrecioUnitario = 8m, Fecha = base1.AddDays(1),    Motivo = MotivoMovimiento.Venta  },
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = base1.AddDays(2),    Motivo = MotivoMovimiento.Compra },
            // p2: 1 entrada
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 20m, PrecioUnitario = 3m, Fecha = base1.AddDays(3),    Motivo = MotivoMovimiento.Compra }
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

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
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p1 = NuevoProducto("ORD001", um, 0m); // ID bajo (creado primero)
        p1.Nombre = "Producto Reciente";
        var p2 = NuevoProducto("ORD002", um, 0m); // ID alto (creado después)
        p2.Nombre = "Producto Viejo";
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();

        var fechaVieja     = DateTime.UtcNow.AddDays(-30);
        var fechaReciente  = DateTime.UtcNow;

        Context.MovimientosStock.AddRange(
            // p2 (ID alto): movimiento viejo
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = fechaVieja,    Motivo = MotivoMovimiento.Compra },
            // p1 (ID bajo): movimiento reciente
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = fechaReciente, Motivo = MotivoMovimiento.Compra }
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

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
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p = NuevoProducto("HM04", um, 0m);
        Context.Productos.Add(p);
        await Context.SaveChangesAsync();

        var fechaMovimiento = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc);
        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId     = p.Id,
            UsuarioId      = usuario.Id,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 5m,
            PrecioUnitario = 10m,
            Fecha          = fechaMovimiento,
            Motivo         = MotivoMovimiento.Compra
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

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
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p = NuevoProducto("RB001", um, 0m);
        Context.Productos.Add(p);
        await Context.SaveChangesAsync();

        var t0 = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 10m, PrecioUnitario = 5m, Fecha = t0,            Motivo = MotivoMovimiento.Compra },
            new MovimientoStock { ProductoId = p.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Cantidad = 5m,  PrecioUnitario = 5m, Fecha = t0.AddHours(1), Motivo = MotivoMovimiento.Compra }
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

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
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p = NuevoProducto("USR001", um, 0m);
        Context.Productos.Add(p);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId     = p.Id,
            UsuarioId      = usuario.Id,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 5m,
            PrecioUnitario = 10m,
            Fecha          = DateTime.UtcNow,
            Motivo         = MotivoMovimiento.Compra
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(ProductoId: p.Id));

        Assert.Single(resultado);
        Assert.Equal("Juan Pérez", resultado[0].UsuarioNombre);
    }

    [Fact]
    public async Task ObtenerHistorialAsync_PopulaUsuarioNombre_FallbackANombreUsuarioSiNoHayNombreCompleto()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario("mgomez"); // NombreCompleto queda null (default del helper)
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p = NuevoProducto("USR002", um, 0m);
        Context.Productos.Add(p);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId     = p.Id,
            UsuarioId      = usuario.Id,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 5m,
            PrecioUnitario = 10m,
            Fecha          = DateTime.UtcNow,
            Motivo         = MotivoMovimiento.Compra
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerHistorialAsync(new HistorialMovimientoFiltro(ProductoId: p.Id));

        Assert.Single(resultado);
        Assert.Equal("mgomez", resultado[0].UsuarioNombre);
    }
}

/// <summary>
/// Variante que inyecta Detalle=null para forzar DbUpdateException DENTRO de la
/// transacción explícita. Replica la estructura del método real (BeginTransaction +
/// ExecuteUpdateAsync + inserts) pero con Detalle inválido, para verificar el rollback
/// completo (incluido el UPDATE de stock). Usada solo en el test C4.
/// </summary>
internal sealed class MovimientoStockRepositoryConDetalleNulo : MovimientoStockRepository
{
    private readonly AppDbContext _ctx;

    public MovimientoStockRepositoryConDetalleNulo(AppDbContext ctx) : base(ctx)
        => _ctx = ctx;

    public override async Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var delta = args.Tipo == TipoMovimiento.Entrada ? args.Cantidad : -args.Cantidad;
        await _ctx.Productos
            .Where(p => p.Id == args.ProductoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual + delta));

        _ctx.MovimientosStock.Add(args.Movimiento);
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RegistroMovimiento,
            Entidad   = "MovimientoStock",
            EntidadId = args.ProductoId,
            Detalle   = null!   // viola NOT NULL → SaveChangesAsync lanza DbUpdateException
        });

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();
        return new ResultadoRegistro(ResultadoRegistroEstado.Ok, args.Movimiento.Id, 0m);
    }
}
