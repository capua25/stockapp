using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class GastoRepository : IGastoRepository
{
    private readonly AppDbContext _ctx;

    public GastoRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<Gasto> ConIncludes() =>
        _ctx.Gastos
            .Include(g => g.Proveedor)
            .Include(g => g.FuenteFinanciamiento)
            .Include(g => g.RubroGasto)
            .Include(g => g.LineaPoa)
            .Include(g => g.Pagos);

    public Task<Gasto?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(g => g.Id == id);

    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura)
        => ConIncludes().FirstOrDefaultAsync(g =>
            g.Activo && g.ProveedorId == proveedorId && g.NumeroFactura == numeroFactura);

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        var query = ConIncludes();

        if (filtro.FechaDesde is not null)
            query = query.Where(g => g.Fecha >= filtro.FechaDesde);
        if (filtro.FechaHasta is not null)
            query = query.Where(g => g.Fecha <= filtro.FechaHasta);
        if (filtro.ProveedorId is not null)
            query = query.Where(g => g.ProveedorId == filtro.ProveedorId);
        if (filtro.FuenteFinanciamientoId is not null)
            query = query.Where(g => g.FuenteFinanciamientoId == filtro.FuenteFinanciamientoId);
        if (filtro.RubroGastoId is not null)
            query = query.Where(g => g.RubroGastoId == filtro.RubroGastoId);
        if (filtro.LineaPoaId is not null)
            query = query.Where(g => g.LineaPoaId == filtro.LineaPoaId);

        return await query
            .OrderByDescending(g => g.Fecha)
            .ThenByDescending(g => g.Id)
            .ToListAsync();
    }

    public async Task<int> AgregarAsync(Gasto gasto)
    {
        _ctx.Gastos.Add(gasto);  // inserta el grafo completo (gasto + pagos)
        await _ctx.SaveChangesAsync();
        return gasto.Id;
    }

    public Task ActualizarAsync(Gasto gasto)
    {
        _ctx.Gastos.Update(gasto);
        return _ctx.SaveChangesAsync();
    }

    public async Task<int> AgregarPagoAsync(PagoGasto pago)
    {
        _ctx.PagosGasto.Add(pago);
        await _ctx.SaveChangesAsync();
        return pago.Id;
    }

    public Task ActualizarPagoAsync(PagoGasto pago)
    {
        _ctx.PagosGasto.Update(pago);
        return _ctx.SaveChangesAsync();
    }

    public async Task<decimal> TotalGastadoLineaFuenteAsync(
        int lineaPoaId, int fuenteFinanciamientoId, int? excluyendoGastoId = null)
        => await _ctx.Gastos
            .Where(g => g.Activo
                        && g.LineaPoaId == lineaPoaId
                        && g.FuenteFinanciamientoId == fuenteFinanciamientoId
                        && (excluyendoGastoId == null || g.Id != excluyendoGastoId))
            .SumAsync(g => (decimal?)g.MontoTotal) ?? 0m;

    public async Task<IReadOnlyList<MovimientoStock>> ObtenerMovimientosAsync(IReadOnlyList<int> movimientoIds)
        => await _ctx.MovimientosStock.Where(m => movimientoIds.Contains(m.Id)).ToListAsync();

    public async Task AsignarGastoAMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
    {
        var movimientos = await _ctx.MovimientosStock
            .Where(m => movimientoIds.Contains(m.Id)).ToListAsync();
        foreach (var movimiento in movimientos)
            movimiento.GastoId = gastoId;
        await _ctx.SaveChangesAsync();
    }

    public async Task DesvincularMovimientosAsync(int gastoId)
    {
        var movimientos = await _ctx.MovimientosStock
            .Where(m => m.GastoId == gastoId).ToListAsync();
        foreach (var movimiento in movimientos)
            movimiento.GastoId = null;
        await _ctx.SaveChangesAsync();
    }
}
