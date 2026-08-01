using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de RegistrarIngresoPorFacturaAtomicoAsync / AnularIngresoPorFacturaAtomicoAsync contra
/// PostgreSQL real (Testcontainers). Task 2 cubre el alta con productos EXISTENTES; Task 3
/// (alta de producto nuevo), Task 4 (precio selectivo) y Task 5 (anulación) agregan tests acá.
/// </summary>
public class MovimientoStockRepositoryIngresoTests : PostgresRepositoryTestBase
{
    private readonly MovimientoStockRepository _repo;

    public MovimientoStockRepositoryIngresoTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new MovimientoStockRepository(Context);
    }

    private static UnidadMedida NuevaUm() => new() { Nombre = "Unidad", Abreviatura = "u" };

    private static Usuario NuevoUsuario() => new()
    {
        NombreUsuario = "admin", HashContrasena = "hash", Rol = RolUsuario.Admin,
        Activo = true, FechaAlta = DateTime.UtcNow,
    };

    private static Proveedor NuevoProveedor(string nombre = "Proveedor Test") => new() { Nombre = nombre };
    private static FuenteFinanciamiento NuevaFuente() => new() { Nombre = "Fuente Test" };
    private static RubroGasto NuevoRubro(int codigo) => new() { Codigo = codigo, Nombre = "Rubro Test" };

    private static Producto NuevoProducto(string codigo, UnidadMedida um, decimal stock = 10m, decimal precioCosto = 5m) => new()
    {
        Codigo = codigo, Nombre = $"Producto {codigo}", UnidadMedida = um,
        PrecioCosto = precioCosto, PrecioVenta = precioCosto * 2, StockActual = stock,
        Activo = true, FechaAlta = DateTime.UtcNow,
    };

    private async Task<(UnidadMedida um, Usuario usuario, Proveedor proveedor, FuenteFinanciamiento fuente, RubroGasto rubro)> SeedMaestrosAsync()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario();
        var proveedor = NuevoProveedor();
        var fuente = NuevaFuente();
        var rubro = NuevoRubro(Random.Shared.Next(1, 1_000_000));
        Context.AddRange(um, usuario, proveedor, fuente, rubro);
        await Context.SaveChangesAsync();
        return (um, usuario, proveedor, fuente, rubro);
    }

    private static Gasto NuevoGasto(Proveedor proveedor, FuenteFinanciamiento fuente, RubroGasto rubro, string? factura = "F-0001") => new()
    {
        ProveedorId = proveedor.Id, NumeroFactura = factura, Detalle = "Compra de insumos",
        Fecha = DateTime.UtcNow, MontoTotal = 500m,
        FuenteFinanciamientoId = fuente.Id, RubroGastoId = rubro.Id,
        CondicionPago = CondicionPago.Contado,
    };

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_TresRenglonesExistentes_PersisteGastoMovimientosYStock()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("ING-1", um, stock: 10m);
        var p2 = NuevoProducto("ING-2", um, stock: 20m);
        var p3 = NuevoProducto("ING-3", um, stock: 0m);
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gasto = NuevoGasto(proveedor, fuente, rubro);
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[]
            {
                new RenglonIngresoFacturaArgs(p1.Id, null, 5m, 90m, false, null),
                new RenglonIngresoFacturaArgs(p2.Id, null, 3m, 50m, false, null),
                new RenglonIngresoFacturaArgs(p3.Id, null, 8m, 20m, false, null),
            },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "Ingreso por factura de prueba");

        var resultado = await _repo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        Assert.True(resultado.GastoId > 0);
        Assert.Equal(3, resultado.MovimientoIds.Count);

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.Gastos.CountAsync());
        var movimientos = await ctx2.MovimientosStock.ToListAsync();
        Assert.Equal(3, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal(resultado.GastoId, m.GastoId));

        Assert.Equal(15m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(23m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.Equal(8m, (await ctx2.Productos.FindAsync(p3.Id))!.StockActual);

        var log = await ctx2.LogsAuditoria.SingleAsync();
        Assert.Equal(44, (int)log.Accion);   // AccionAuditada.IngresoPorFactura
        Assert.Equal(resultado.GastoId, log.EntidadId);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_FacturaDuplicada_LanzaReglaDeNegocioYNoEscribeNada()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var existente = NuevoGasto(proveedor, fuente, rubro, factura: "DUP-01");
        Context.Gastos.Add(existente);
        var producto = NuevoProducto("ING-DUP", um);
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gastoNuevo = NuevoGasto(proveedor, fuente, rubro, factura: "DUP-01");
        var args = new IngresoPorFacturaArgs(
            Gasto: gastoNuevo,
            Renglones: new[] { new RenglonIngresoFacturaArgs(producto.Id, null, 2m, 10m, false, null) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "intento duplicado");

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _repo.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.Gastos.CountAsync());     // solo el existente
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(producto.StockActual, (await ctx2.Productos.FindAsync(producto.Id))!.StockActual);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_FallaAlEscribirAuditoria_RevierteLosTresRenglones()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("ING-R1", um, stock: 10m);
        var p2 = NuevoProducto("ING-R2", um, stock: 10m);
        var p3 = NuevoProducto("ING-R3", um, stock: 10m);
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repoRoto = new MovimientoStockRepositoryIngresoConDetalleNulo(Context);
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "ROLLBACK-01");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[]
            {
                new RenglonIngresoFacturaArgs(p1.Id, null, 1m, 10m, false, null),
                new RenglonIngresoFacturaArgs(p2.Id, null, 2m, 10m, false, null),
                new RenglonIngresoFacturaArgs(p3.Id, null, 3m, 10m, false, null),
            },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "se sobreescribe con null en el repo roto");

        await Assert.ThrowsAsync<DbUpdateException>(() => repoRoto.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.Gastos.CountAsync());
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p3.Id))!.StockActual);
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_ProductoNuevo_SeCreaConStockIgualALaCantidad()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var productoNuevo = new Producto
        {
            Codigo = "NUEVO-1", Nombre = "Producto recién creado", UnidadMedidaId = um.Id,
            PrecioCosto = 45m, PrecioVenta = 90m, StockActual = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
        };

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "NUEVO-FAC-1");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[] { new RenglonIngresoFacturaArgs(null, productoNuevo, 12m, 45m, false, null) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "Alta de producto nuevo en el lote");

        var resultado = await _repo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        await using var ctx2 = Fixture.CrearContexto();
        var creado = await ctx2.Productos.SingleAsync(p => p.Codigo == "NUEVO-1");
        Assert.Equal(12m, creado.StockActual);
        Assert.Equal(45m, creado.PrecioCosto);

        var movimiento = await ctx2.MovimientosStock.SingleAsync();
        Assert.Equal(creado.Id, movimiento.ProductoId);
        Assert.Equal(resultado.GastoId, movimiento.GastoId);
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_ProductoNuevoConCodigoDuplicado_RollbackTotal()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var existente = NuevoProducto("DUP-PROD", um);
        Context.Productos.Add(existente);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var productoNuevo = new Producto
        {
            Codigo = "DUP-PROD", Nombre = "Choca contra el existente", UnidadMedidaId = um.Id,
            PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
        };
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "DUP-PROD-FAC");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[] { new RenglonIngresoFacturaArgs(null, productoNuevo, 5m, 10m, false, null) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "código duplicado");

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => _repo.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.Productos.CountAsync());   // solo el existente
        Assert.Equal(0, await ctx2.Gastos.CountAsync());
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
    }
}

/// <summary>
/// Variante que inyecta Detalle=null en el LogAuditoria final para forzar DbUpdateException
/// DENTRO de la transacción explícita, después de que los 3 renglones ya fueron procesados —
/// verifica que el rollback revierte también los ExecuteUpdateAsync de stock ya ejecutados.
/// Mismo patrón que MovimientoStockRepositoryConDetalleNulo (MovimientoStockRepositoryTests.cs).
/// </summary>
internal sealed class MovimientoStockRepositoryIngresoConDetalleNulo : MovimientoStockRepository
{
    private readonly AppDbContext _ctx;
    public MovimientoStockRepositoryIngresoConDetalleNulo(AppDbContext ctx) : base(ctx) => _ctx = ctx;

    public override async Task<ResultadoIngresoPorFactura> RegistrarIngresoPorFacturaAtomicoAsync(IngresoPorFacturaArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        _ctx.Gastos.Add(args.Gasto);
        var movimientos = new List<MovimientoStock>();
        foreach (var renglon in args.Renglones)
        {
            var productoId = renglon.ProductoId!.Value;
            await _ctx.Productos.Where(p => p.Id == productoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual + renglon.Cantidad));
            var movimiento = new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = args.UsuarioId, Tipo = TipoMovimiento.Entrada,
                Motivo = MotivoMovimiento.Compra, Cantidad = renglon.Cantidad,
                PrecioUnitario = renglon.PrecioUnitario, Fecha = DateTime.UtcNow, Gasto = args.Gasto,
            };
            movimientos.Add(movimiento);
            _ctx.MovimientosStock.Add(movimiento);
        }
        await _ctx.SaveChangesAsync();

        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId, Fecha = DateTime.UtcNow,
            Accion = AccionAuditada.IngresoPorFactura, Entidad = "Gasto", EntidadId = args.Gasto.Id,
            Detalle = null!,   // viola NOT NULL → DbUpdateException dentro de la transacción
        });
        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoIngresoPorFactura(args.Gasto.Id, movimientos.Select(m => m.Id).ToList());
    }
}
