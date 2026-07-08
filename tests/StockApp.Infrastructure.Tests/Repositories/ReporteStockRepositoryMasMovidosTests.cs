using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de integración para ReporteStockRepository.ObtenerMasMovidosAsync contra PostgreSQL real.
/// </summary>
public class ReporteStockRepositoryMasMovidosTests : PostgresRepositoryTestBase
{
    private readonly ReporteStockRepository _repo;

    public ReporteStockRepositoryMasMovidosTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ReporteStockRepository(Context);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static UnidadMedida NuevaUm() => new() { Nombre = "Unidad", Abreviatura = "u" };

    private static Usuario NuevoUsuario(string nombre = "rep_user") => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "hash",
        Rol            = RolUsuario.Admin,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow
    };

    private static Producto NuevoProducto(string codigo, string nombre, UnidadMedida um) => new()
    {
        Codigo      = codigo,
        Nombre      = nombre,
        UnidadMedida = um,
        PrecioCosto = 10m,
        PrecioVenta = 20m,
        StockActual = 0m,
        Activo      = true,
        FechaAlta   = DateTime.UtcNow
    };

    private MovimientoStock Mov(int productoId, int usuarioId, decimal cantidad, DateTime fecha) => new()
    {
        ProductoId     = productoId,
        UsuarioId      = usuarioId,
        Tipo           = TipoMovimiento.Entrada,
        Cantidad       = cantidad,
        PrecioUnitario = 5m,
        Fecha          = fecha,
        Motivo         = MotivoMovimiento.Compra
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerMasMovidosAsync_OrdenadoPorVolumenDesc()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario();
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var pA = NuevoProducto("PA", "Producto A", um);
        var pB = NuevoProducto("PB", "Producto B", um);
        var pC = NuevoProducto("PC", "Producto C", um);
        Context.Productos.AddRange(pA, pB, pC);
        await Context.SaveChangesAsync();

        var t = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        // pA: 2 movs, volumen 10+5 = 15
        // pB: 3 movs, volumen 20+20+20 = 60  (mayor)
        // pC: 1 mov,  volumen 8
        Context.MovimientosStock.AddRange(
            Mov(pA.Id, usuario.Id, 10m, t),
            Mov(pA.Id, usuario.Id, 5m,  t.AddHours(1)),
            Mov(pB.Id, usuario.Id, 20m, t),
            Mov(pB.Id, usuario.Id, 20m, t.AddHours(1)),
            Mov(pB.Id, usuario.Id, 20m, t.AddHours(2)),
            Mov(pC.Id, usuario.Id, 8m,  t)
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerMasMovidosAsync(null, null, topN: 10);

        Assert.Equal(3, resultado.Count);

        // Orden DESC por VolumenTotal: pB (60), pA (15), pC (8)
        Assert.Equal("PB", resultado[0].Codigo);
        Assert.Equal(60m,  resultado[0].VolumenTotal);
        Assert.Equal(3,    resultado[0].CantidadMovimientos);
        Assert.Equal("Producto B", resultado[0].Nombre);

        Assert.Equal("PA", resultado[1].Codigo);
        Assert.Equal(15m,  resultado[1].VolumenTotal);
        Assert.Equal(2,    resultado[1].CantidadMovimientos);

        Assert.Equal("PC", resultado[2].Codigo);
        Assert.Equal(8m,   resultado[2].VolumenTotal);
        Assert.Equal(1,    resultado[2].CantidadMovimientos);
    }

    [Fact]
    public async Task ObtenerMasMovidosAsync_TopNRespetado()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario();
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p1 = NuevoProducto("P1", "Uno",    um);
        var p2 = NuevoProducto("P2", "Dos",    um);
        var p3 = NuevoProducto("P3", "Tres",   um);
        Context.Productos.AddRange(p1, p2, p3);
        await Context.SaveChangesAsync();

        var t = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        Context.MovimientosStock.AddRange(
            Mov(p1.Id, usuario.Id, 30m, t),   // volumen 30
            Mov(p2.Id, usuario.Id, 20m, t),   // volumen 20
            Mov(p3.Id, usuario.Id, 10m, t)    // volumen 10
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ObtenerMasMovidosAsync(null, null, topN: 2);

        // Solo 2 (los de mayor volumen): P1, P2
        Assert.Equal(2, resultado.Count);
        Assert.Equal("P1", resultado[0].Codigo);
        Assert.Equal("P2", resultado[1].Codigo);
        Assert.DoesNotContain(resultado, x => x.Codigo == "P3");
    }

    [Fact]
    public async Task ObtenerMasMovidosAsync_FechaHastaFinDeDia()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario();
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p = NuevoProducto("FH", "Fecha", um);
        Context.Productos.Add(p);
        await Context.SaveChangesAsync();

        // Movimiento a las 18:00hs del 2026-06-10 (debe INCLUIRSE)
        var fechaMovDentro = new DateTime(2026, 6, 10, 18, 0, 0, DateTimeKind.Utc);
        // Movimiento a las 00:00hs del 2026-06-11 (debe EXCLUIRSE - día siguiente)
        var fechaMovFuera = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc);

        Context.MovimientosStock.AddRange(
            Mov(p.Id, usuario.Id, 7m, fechaMovDentro),
            Mov(p.Id, usuario.Id, 3m, fechaMovFuera)
        );
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        // FechaHasta = 2026-06-10 a medianoche (00:00) → el ajuste a fin de día debe
        // incluir todos los movimientos del 10 (18:00hs) pero EXCLUIR el del 11 (00:00hs).
        var fechaHasta = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        var resultado = await _repo.ObtenerMasMovidosAsync(null, fechaHasta, topN: 10);

        // Solo el movimiento del día dentro del rango debe estar incluido
        Assert.Single(resultado);
        Assert.Equal("FH", resultado[0].Codigo);
        Assert.Equal(7m,   resultado[0].VolumenTotal);      // Solo el del 10 (7m)
        Assert.Equal(1,    resultado[0].CantidadMovimientos); // Un solo movimiento
    }

    [Fact]
    public async Task ObtenerMasMovidosAsync_SinMovimientos_ListaVacia()
    {
        var um = NuevaUm();
        var usuario = NuevoUsuario();
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var p = NuevoProducto("EMPTY", "Sin movs", um);
        Context.Productos.Add(p);
        await Context.SaveChangesAsync();

        // Movimiento fuera del rango consultado
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Context.MovimientosStock.Add(Mov(p.Id, usuario.Id, 5m, t));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        // Rango sin movimientos
        var desde = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc);

        var resultado = await _repo.ObtenerMasMovidosAsync(desde, hasta, topN: 10);

        Assert.Empty(resultado);
    }
}
