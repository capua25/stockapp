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
        PrecioCosto = precioCosto, StockActual = stock,
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
            PrecioCosto = 45m, StockActual = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
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
            PrecioCosto = 10m, StockActual = 0m, Activo = true, FechaAlta = DateTime.UtcNow,
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

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_SoloActualizaLosTildados()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("PRC-1", um, stock: 10m, precioCosto: 10m);
        var p2 = NuevoProducto("PRC-2", um, stock: 10m, precioCosto: 10m);
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "PRC-FAC-1");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[]
            {
                new RenglonIngresoFacturaArgs(p1.Id, null, 2m, 15m, true, 10m),   // se actualiza
                new RenglonIngresoFacturaArgs(p2.Id, null, 2m, 15m, false, null), // NO se actualiza
            },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "precio selectivo");

        await _repo.RegistrarIngresoPorFacturaAtomicoAsync(args);

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(15m, (await ctx2.Productos.FindAsync(p1.Id))!.PrecioCosto);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p2.Id))!.PrecioCosto);   // intacto

        var logCambioPrecio = await ctx2.LogsAuditoria.SingleAsync(l => l.Accion == AccionAuditada.CambioPrecio);
        Assert.Equal(p1.Id, logCambioPrecio.EntidadId);
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_ConStockSuficiente_GeneraSalidasEspejoYAnulaElGasto()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("ANU-1", um, stock: 20m);
        var p2 = NuevoProducto("ANU-2", um, stock: 20m);
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "ANU-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 8m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Anulación de prueba");

        Assert.Equal(ResultadoAnulacionIngresoEstado.Ok, resultado.Estado);

        await using var ctx2 = Fixture.CrearContexto();
        var salidas = await ctx2.MovimientosStock.Where(m => m.Tipo == TipoMovimiento.Salida).ToListAsync();
        Assert.Equal(2, salidas.Count);
        Assert.All(salidas, s => Assert.Equal(MotivoMovimiento.Ajuste, s.Motivo));
        // Fix 6 (revisión final, decisión 6 del spec): la salida espejo no lleva GastoId, así que
        // el comentario es el único rastro que la vincula con la factura anulada.
        Assert.All(salidas, s => Assert.Equal(
            $"Anulación de ingreso por factura '{gasto.NumeroFactura}' (Gasto {gasto.Id})", s.Comentario));

        Assert.Equal(12m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);   // 20 - 8
        Assert.Equal(15m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);   // 20 - 5

        var gastoFresh = await ctx2.Gastos.FindAsync(gasto.Id);
        Assert.False(gastoFresh!.Activo);

        var log = await ctx2.LogsAuditoria.SingleAsync();
        Assert.Equal(45, (int)log.Accion);   // AccionAuditada.AnulacionIngresoPorFactura
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_StockInsuficienteEnUnoDeTres_NoEscribeNadaYNombraElProducto()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("INS-1", um, stock: 10m);
        var p2 = NuevoProducto("INS-2", um, stock: 10m);
        var p3 = NuevoProducto("INS-3", um, stock: 2m);   // insuficiente: se consumió parte
        p3.Nombre = "Producto consumido";
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "INS-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p3.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Anulación con faltante");

        Assert.Equal(ResultadoAnulacionIngresoEstado.StockInsuficiente, resultado.Estado);
        var faltante = Assert.Single(resultado.Faltantes);
        Assert.Equal("Producto consumido", faltante.ProductoNombre);
        Assert.Equal(2m, faltante.StockActual);
        Assert.Equal(5m, faltante.CantidadNecesaria);

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(3, await ctx2.MovimientosStock.CountAsync());   // solo las 3 entradas originales
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.Equal(2m, (await ctx2.Productos.FindAsync(p3.Id))!.StockActual);
        Assert.True((await ctx2.Gastos.FindAsync(gasto.Id))!.Activo);
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_GastoConMovimientosAsociadosPorElFlujoViejo_TambienSeAnula()
    {
        // Decisión 9 del spec: la anulación aplica a CUALQUIER gasto con movimientos asociados,
        // no solo a los creados por esta pantalla — cubre el vínculo hecho a mano desde
        // GastoService.AsociarMovimientosAsync (flujo "Asociar factura" existente).
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var producto = NuevoProducto("VIEJO-1", um, stock: 15m);
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "VIEJO-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada,
            Motivo = MotivoMovimiento.Compra, Cantidad = 6m, PrecioUnitario = 10m,
            Fecha = DateTime.UtcNow, GastoId = gasto.Id,   // vínculo hecho por el flujo viejo
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Anulación de vínculo viejo");

        Assert.Equal(ResultadoAnulacionIngresoEstado.Ok, resultado.Estado);
        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(9m, (await ctx2.Productos.FindAsync(producto.Id))!.StockActual);   // 15 - 6
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_LlamadoDosVecesParaElMismoGasto_SoloLaPrimeraEscribeYLaSegundaFalla()
    {
        // Ronda de correcciones 1, Hallazgo 1: el único guardia contra doble anulación era el
        // `if (!gasto.Activo)` del service, leído ANTES de entrar a la transacción — no cerraba
        // la ventana de concurrencia. Este test simula la segunda llamada "ganando la carrera"
        // reusando el mismo _repo/Context: si el gate bajo lock (FOR UPDATE + re-chequeo de
        // Activo) no existiera, la segunda llamada volvería a encontrar las mismas entradas
        // originales (las salidas espejo no llevan GastoId) y escribiría un segundo juego de
        // salidas, duplicando el débito de stock y la auditoría.
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var producto = NuevoProducto("DBL-1", um, stock: 20m);
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "DBL-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada,
            Motivo = MotivoMovimiento.Compra, Cantidad = 6m, PrecioUnitario = 10m,
            Fecha = DateTime.UtcNow, GastoId = gasto.Id,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var primera = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Primera anulación");
        Assert.Equal(ResultadoAnulacionIngresoEstado.Ok, primera.Estado);

        Context.ChangeTracker.Clear();
        var segunda = await _repo.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "Segunda anulación (debe fallar)");
        Assert.Equal(ResultadoAnulacionIngresoEstado.GastoYaAnulado, segunda.Estado);

        await using var ctx2 = Fixture.CrearContexto();
        var salidas = await ctx2.MovimientosStock.Where(m => m.Tipo == TipoMovimiento.Salida).ToListAsync();
        Assert.Single(salidas);   // UN solo juego de salidas espejo, no dos

        Assert.Equal(14m, (await ctx2.Productos.FindAsync(producto.Id))!.StockActual);   // 20 - 6, descontado UNA sola vez

        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync(l => l.Accion == AccionAuditada.AnulacionIngresoPorFactura));
    }

    [Fact]
    public async Task AnularIngresoPorFacturaAtomicoAsync_FallaAlEscribirAuditoria_RevierteTodo()
    {
        // Ronda de correcciones 1, Hallazgo 2: mismo patrón que
        // RegistrarIngresoPorFacturaAtomicoAsync_FallaAlEscribirAuditoria_RevierteLosTresRenglones
        // (MovimientoStockRepositoryIngresoConDetalleNulo) pero para la anulación: fuerza un
        // DbUpdateException DESPUÉS de que ya se ejecutaron los ExecuteUpdateAsync de stock de
        // ambos productos, y verifica que la transacción explícita revierte TODO — ni salidas
        // espejo, ni stock descontado, ni Gasto.Activo=false, ni auditoría.
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("RB-ANU-1", um, stock: 20m);
        var p2 = NuevoProducto("RB-ANU-2", um, stock: 20m);
        Context.Productos.AddRange(p1, p2);
        await Context.SaveChangesAsync();

        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "RB-ANU-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.AddRange(
            new MovimientoStock { ProductoId = p1.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 4m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id },
            new MovimientoStock { ProductoId = p2.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra, Cantidad = 3m, PrecioUnitario = 10m, Fecha = DateTime.UtcNow, GastoId = gasto.Id });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repoRoto = new MovimientoStockRepositoryAnulacionConDetalleNulo(Context);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            repoRoto.AnularIngresoPorFacturaAtomicoAsync(gasto.Id, usuario.Id, "se sobreescribe con null en el repo roto"));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync(m => m.Tipo == TipoMovimiento.Salida));
        Assert.Equal(20m, (await ctx2.Productos.FindAsync(p1.Id))!.StockActual);
        Assert.Equal(20m, (await ctx2.Productos.FindAsync(p2.Id))!.StockActual);
        Assert.True((await ctx2.Gastos.FindAsync(gasto.Id))!.Activo);
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
    }

    [Fact]
    public async Task RegistrarIngresoPorFacturaAtomicoAsync_RollbackRevierteTambienLosPrecios()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var p1 = NuevoProducto("PRC-RB-1", um, stock: 10m, precioCosto: 10m);
        Context.Productos.Add(p1);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var repoRoto = new MovimientoStockRepositoryIngresoConDetalleNulo(Context);
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "PRC-RB-FAC");
        var args = new IngresoPorFacturaArgs(
            Gasto: gasto,
            Renglones: new[] { new RenglonIngresoFacturaArgs(p1.Id, null, 1m, 99m, true, 10m) },
            UsuarioId: usuario.Id,
            DetalleAuditoria: "se sobreescribe con null en el repo roto");

        await Assert.ThrowsAsync<DbUpdateException>(() => repoRoto.RegistrarIngresoPorFacturaAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(10m, (await ctx2.Productos.FindAsync(p1.Id))!.PrecioCosto);   // sin cambios
    }

    [Fact]
    public async Task ExistenMovimientosDeGastoAsync_SinMovimientos_DevuelveFalse()
    {
        var (_, _, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "EXIST-FAC-1");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var existen = await _repo.ExistenMovimientosDeGastoAsync(gasto.Id);

        Assert.False(existen);
    }

    [Fact]
    public async Task ExistenMovimientosDeGastoAsync_ConUnMovimiento_DevuelveTrue()
    {
        var (um, usuario, proveedor, fuente, rubro) = await SeedMaestrosAsync();
        var producto = NuevoProducto("EXIST-1", um, stock: 5m);
        Context.Productos.Add(producto);
        var gasto = NuevoGasto(proveedor, fuente, rubro, factura: "EXIST-FAC-2");
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();

        Context.MovimientosStock.Add(new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Entrada,
            Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m,
            Fecha = DateTime.UtcNow, GastoId = gasto.Id,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var existen = await _repo.ExistenMovimientosDeGastoAsync(gasto.Id);

        Assert.True(existen);
    }
}

/// <summary>
/// Variante que inyecta Detalle=null en el LogAuditoria final para forzar DbUpdateException
/// DENTRO de la transacción explícita, después de que los renglones ya fueron procesados —
/// verifica que el rollback revierte también los ExecuteUpdateAsync de stock (y, desde Task 4,
/// de precio) ya ejecutados. Mismo patrón que MovimientoStockRepositoryConDetalleNulo
/// (MovimientoStockRepositoryTests.cs).
///
/// Fix 7 (revisión final): antes, este helper reimplementaba a mano el cuerpo completo de
/// RegistrarIngresoPorFacturaAtomicoAsync (~50 líneas) — copia que YA había divergido del
/// original (no replicaba la rama de producto nuevo ni los catch de DbUpdateException). Ahora
/// sobrescribe únicamente el seam <see cref="MovimientoStockRepository.CrearLogAuditoriaIngreso"/>
/// que el método real usa para construir el LogAuditoria final; todo el resto del método (stock,
/// precio, producto nuevo, catch de violación de unicidad) corre el código de producción real.
/// </summary>
internal sealed class MovimientoStockRepositoryIngresoConDetalleNulo : MovimientoStockRepository
{
    public MovimientoStockRepositoryIngresoConDetalleNulo(AppDbContext ctx) : base(ctx)
    {
    }

    protected override LogAuditoria CrearLogAuditoriaIngreso(IngresoPorFacturaArgs args) => new()
    {
        UsuarioId = args.UsuarioId,
        Fecha     = DateTime.UtcNow,
        Accion    = AccionAuditada.IngresoPorFactura,
        Entidad   = "Gasto",
        EntidadId = args.Gasto.Id,
        Detalle   = null!,   // viola NOT NULL → DbUpdateException dentro de la transacción
    };
}

/// <summary>
/// Variante de AnularIngresoPorFacturaAtomicoAsync que inyecta Detalle=null en el LogAuditoria
/// final para forzar DbUpdateException DENTRO de la transacción explícita, después de que ya se
/// ejecutaron los ExecuteUpdateAsync de stock de ambos productos — verifica que el rollback
/// revierte también esos deltas ya aplicados (mismo patrón que
/// MovimientoStockRepositoryIngresoConDetalleNulo, arriba).
///
/// Evidencia de mutación (ronda de correcciones 1, Hallazgo 2): se rompió la atomicidad a
/// propósito insertando un `await tx.CommitAsync()` temprano (justo después del foreach de
/// ExecuteUpdateAsync, antes del update de Gasto.Activo y del insert de auditoría). Con esa
/// mutación el test
/// AnularIngresoPorFacturaAtomicoAsync_FallaAlEscribirAuditoria_RevierteTodo se puso en ROJO:
/// Assert.Equal(20m, ...StockActual) falló observando 16m para p1 (20 - 4) y 17m para p2
/// (20 - 3) — el descuento de stock había quedado committeado a pesar de que el método seguía
/// lanzando DbUpdateException. Se revirtió la mutación (se sacó el CommitAsync temprano) y el
/// test volvió a VERDE. Detalle completo en task-5-report.md, sección "Ronda de correcciones 1".
///
/// Fix 7 (revisión final): mismo refactor que MovimientoStockRepositoryIngresoConDetalleNulo —
/// este helper ahora solo sobrescribe el seam
/// <see cref="MovimientoStockRepository.CrearLogAuditoriaAnulacion"/> en vez de duplicar el
/// cuerpo entero de AnularIngresoPorFacturaAtomicoAsync. Se repitió la prueba de mutación
/// (commit temprano) contra esta versión refactorizada para confirmar que el seam sigue
/// probando una atomicidad real y no solo pasando trivialmente — ver fix-final-report.md.
/// </summary>
internal sealed class MovimientoStockRepositoryAnulacionConDetalleNulo : MovimientoStockRepository
{
    public MovimientoStockRepositoryAnulacionConDetalleNulo(AppDbContext ctx) : base(ctx)
    {
    }

    protected override LogAuditoria CrearLogAuditoriaAnulacion(int gastoId, int usuarioId, string detalleAuditoria) => new()
    {
        UsuarioId = usuarioId,
        Fecha     = DateTime.UtcNow,
        Accion    = AccionAuditada.AnulacionIngresoPorFactura,
        Entidad   = "Gasto",
        EntidadId = gastoId,
        Detalle   = null!,   // viola NOT NULL → DbUpdateException dentro de la transacción
    };
}
