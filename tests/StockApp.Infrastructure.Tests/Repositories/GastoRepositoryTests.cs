using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class GastoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly GastoRepository _repo;

    public GastoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new GastoRepository(Context);
    }

    // ── Seeds mínimos: el gasto exige proveedor + fuente + rubro por FK ──────

    private async Task<(int proveedorId, int fuenteId, int rubroId)> SeedMaestrosAsync()
    {
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro test" };
        Context.AddRange(proveedor, fuente, rubro);
        await Context.SaveChangesAsync();
        return (proveedor.Id, fuente.Id, rubro.Id);
    }

    private async Task<int> SeedLineaPoaAsync(int fuenteId, decimal asignado)
    {
        var linea = new LineaPoa
        {
            Nombre = $"Linea {Guid.NewGuid():N}", Programa = "Test", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteId, Monto = asignado } },
        };
        Context.Add(linea);
        await Context.SaveChangesAsync();
        return linea.Id;
    }

    private static Gasto NuevoGasto(int proveedorId, int fuenteId, int rubroId, DateTime fecha,
        decimal monto = 1000m, string? factura = null, int? lineaPoaId = null) => new()
    {
        ProveedorId = proveedorId,
        NumeroFactura = factura,
        Detalle = "Gasto de prueba",
        Fecha = fecha,
        MontoTotal = monto,
        FuenteFinanciamientoId = fuenteId,
        RubroGastoId = rubroId,
        LineaPoaId = lineaPoaId,
        CondicionPago = CondicionPago.Credito,
        FechaVencimiento = fecha.AddDays(30),
    };

    [Fact]
    public async Task AgregarAsync_ConPagos_Y_ObtenerPorId_TraeGrafoCompleto()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var fecha = DateTime.UtcNow;
        var gasto = NuevoGasto(proveedorId, fuenteId, rubroId, fecha, factura: "A-0001");
        gasto.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 400.5000m, Nota = "seña" });

        var id = await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("A-0001", found!.NumeroFactura);
        Assert.NotNull(found.Proveedor);
        Assert.NotNull(found.FuenteFinanciamiento);
        Assert.NotNull(found.RubroGasto);
        var pago = Assert.Single(found.Pagos);
        Assert.Equal(400.5000m, pago.Monto);
        Assert.Equal(400.5000m, found.TotalPagado);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorFechasYProveedor_OrdenaFechaDesc()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var (otroProveedorId, _, _) = await SeedMaestrosAsync();
        var hoy = DateTime.UtcNow;

        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy.AddDays(-10)));
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy.AddDays(-1)));
        await _repo.AgregarAsync(NuevoGasto(otroProveedorId, fuenteId, rubroId, hoy.AddDays(-1)));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarAsync(new GastoFiltro(
            FechaDesde: hoy.AddDays(-5), ProveedorId: proveedorId));

        var gasto = Assert.Single(result);
        Assert.Equal(proveedorId, gasto.ProveedorId);

        var todos = await _repo.ListarAsync(new GastoFiltro(ProveedorId: proveedorId));
        Assert.Equal(2, todos.Count);
        Assert.True(todos[0].Fecha > todos[1].Fecha); // desc
    }

    [Fact]
    public async Task ListarAsync_FiltraPorLineaPoa()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var lineaId = await SeedLineaPoaAsync(fuenteId, 10000m);
        var hoy = DateTime.UtcNow;

        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, lineaPoaId: lineaId));
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarAsync(new GastoFiltro(LineaPoaId: lineaId));

        var gasto = Assert.Single(result);
        Assert.Equal(lineaId, gasto.LineaPoaId);
        Assert.NotNull(gasto.LineaPoa);
    }

    [Fact]
    public async Task ObtenerPorProveedorYFactura_SoloActivos()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var hoy = DateTime.UtcNow;
        var anulado = NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "B-0100");
        anulado.Activo = false;
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        Assert.Null(await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, "B-0100"));

        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "B-0100"));
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, "B-0100");
        Assert.NotNull(found);
        Assert.True(found!.Activo);
    }

    [Fact]
    public async Task TotalGastadoLineaFuente_SumaSoloActivosDeEsaFuente_YExcluye()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var (_, otraFuenteId, _) = await SeedMaestrosAsync();
        var lineaId = await SeedLineaPoaAsync(fuenteId, 10000m);
        var hoy = DateTime.UtcNow;

        var id1 = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, 1000m, lineaPoaId: lineaId));
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, 2000m, lineaPoaId: lineaId));
        var anulado = NuevoGasto(proveedorId, fuenteId, rubroId, hoy, 5000m, lineaPoaId: lineaId);
        anulado.Activo = false;
        await _repo.AgregarAsync(anulado);
        await _repo.AgregarAsync(NuevoGasto(proveedorId, otraFuenteId, rubroId, hoy, 7000m, lineaPoaId: lineaId));
        Context.ChangeTracker.Clear();

        Assert.Equal(3000m, await _repo.TotalGastadoLineaFuenteAsync(lineaId, fuenteId));
        Assert.Equal(2000m, await _repo.TotalGastadoLineaFuenteAsync(lineaId, fuenteId, excluyendoGastoId: id1));
    }

    // ── Bug real (verificación orgánica): el mensaje de sobrepago mostraba el decimal
    // crudo, ej. "(799.5000)", en vez del formato moneda es-UY de las grillas ("$ 799,50").

    [Fact]
    public async Task RegistrarPagoAtomico_Sobrepago_MensajeConMontoFormateadoEsUy()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var id = await _repo.AgregarAsync(
            NuevoGasto(proveedorId, fuenteId, rubroId, DateTime.UtcNow, monto: 500m));
        Context.ChangeTracker.Clear();

        var pagoQueSobrepasa = new PagoGasto
        {
            GastoId = id, Fecha = DateTime.UtcNow, Monto = 799.5000m,
        };

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _repo.RegistrarPagoAtomicoAsync(pagoQueSobrepasa));

        Assert.Contains("$ 799,50", ex.Message);
        Assert.DoesNotContain("799.5000", ex.Message);
    }

    [Fact]
    public async Task AgregarPago_Y_AnularPago_Roundtrip()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var id = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, DateTime.UtcNow));
        Context.ChangeTracker.Clear();

        var nuevoPago = new PagoGasto
        {
            GastoId = id, Fecha = DateTime.UtcNow, Monto = 300m, Nota = "primer pago",
        };
        Context.Add(nuevoPago);
        await Context.SaveChangesAsync();
        var pagoId = nuevoPago.Id;
        Context.ChangeTracker.Clear();

        var gasto = await _repo.ObtenerPorIdAsync(id);
        var pago = Assert.Single(gasto!.Pagos);
        Assert.Equal(pagoId, pago.Id);
        Assert.Equal(300m, gasto.TotalPagado);

        pago.Activo = false;
        await _repo.ActualizarPagoAsync(pago);
        Context.ChangeTracker.Clear();

        var releido = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal(0m, releido!.TotalPagado);
    }

    // ── I2: índice único parcial proveedor+factura (gastos activos) ─────────

    [Fact]
    public async Task AgregarAsync_FacturaDuplicadaProveedorActivo_MapeaAReglaDeNegocio()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var hoy = DateTime.UtcNow;
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "C-0001"));
        Context.ChangeTracker.Clear();

        // El índice único parcial (Activo=TRUE) es quien realmente bloquea esto en BD;
        // el catch de GastoRepository lo mapea a 409 en vez de dejar pasar el 500 crudo.
        await Assert.ThrowsAsync<ReglaDeNegocioException>(() =>
            _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "C-0001")));
    }

    [Fact]
    public async Task AgregarAsync_FacturaDuplicadaPeroGastoOriginalAnulado_PermiteReusarFactura()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var hoy = DateTime.UtcNow;
        var anulado = NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "C-0002");
        anulado.Activo = false;
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        // El índice es PARCIAL (solo Activo=TRUE): un gasto anulado libera su factura.
        var id = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "C-0002"));

        Assert.True(id > 0);
    }

    [Fact]
    public async Task AsignarYDesvincularMovimientos_ActualizaGastoId()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();

        // Seed de un movimiento de stock real (exige unidad + producto + usuario)
        var unidad = new UnidadMedida { Nombre = $"Unidad {Guid.NewGuid():N}", Abreviatura = Guid.NewGuid().ToString("N")[..8] };
        var usuario = new Usuario { NombreUsuario = $"user{Guid.NewGuid():N}"[..20], HashContrasena = "x", Rol = RolUsuario.Operador };
        Context.AddRange(unidad, usuario);
        await Context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Prod test",
            UnidadMedidaId = unidad.Id,
        };
        Context.Add(producto);
        await Context.SaveChangesAsync();
        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id,
            Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra,
            Cantidad = 5m, PrecioUnitario = 100m, Fecha = DateTime.UtcNow,
        };
        Context.Add(movimiento);
        await Context.SaveChangesAsync();

        var gastoId = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, DateTime.UtcNow));
        Context.ChangeTracker.Clear();

        await _repo.AsignarGastoAMovimientosAsync(gastoId, new[] { movimiento.Id });
        Context.ChangeTracker.Clear();

        var vinculados = await _repo.ObtenerMovimientosAsync(new[] { movimiento.Id });
        Assert.Equal(gastoId, Assert.Single(vinculados).GastoId);

        await _repo.DesvincularMovimientosAsync(gastoId);
        Context.ChangeTracker.Clear();

        var desvinculados = await _repo.ObtenerMovimientosAsync(new[] { movimiento.Id });
        Assert.Null(Assert.Single(desvinculados).GastoId);
    }
}
