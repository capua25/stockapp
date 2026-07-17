using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class GastoRepository : IGastoRepository
{
    private readonly AppDbContext _ctx;

    /// <summary>
    /// Cultura FIJA es-UY para formatear montos en mensajes de excepción (bug real:
    /// "El pago (799.5000) supera..." mostraba el decimal crudo). Infrastructure no puede
    /// referenciar Presentation.Converters.MonedaConverter, así que se duplica el mismo
    /// criterio (patrón ya usado en PagosGastoViewModel.CulturaMonto y MonedaConverter).
    /// </summary>
    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                CurrencySymbol = "$",
                CurrencyDecimalDigits = 2,
                CurrencyDecimalSeparator = ",",
                CurrencyGroupSeparator = ".",
                CurrencyPositivePattern = 2,
                CurrencyNegativePattern = 9,
            };
        }
    }

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
        try
        {
            _ctx.Gastos.Add(gasto);  // inserta el grafo completo (gasto + pagos)
            await _ctx.SaveChangesAsync();
            return gasto.Id;
        }
        catch (DbUpdateException ex) when (EsViolacionFacturaUnica(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe la factura '{gasto.NumeroFactura}' para ese proveedor.");
        }
    }

    public async Task ActualizarAsync(Gasto gasto)
    {
        try
        {
            _ctx.Gastos.Update(gasto);
            await _ctx.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (EsViolacionFacturaUnica(ex))
        {
            throw new ReglaDeNegocioException(
                $"Ya existe la factura '{gasto.NumeroFactura}' para ese proveedor.");
        }
    }

    /// <summary>
    /// I2 (review final f2-gastos): <c>GastoService.ValidarFacturaUnicaAsync</c> (check-then-act
    /// en memoria) da el 409 con mensaje lindo en el camino feliz; el índice único PARCIAL en BD
    /// (migración UniqueFacturaProveedorGastosActivos, Activo=TRUE AND NumeroFactura NOT NULL)
    /// cierra la carrera real de dos altas concurrentes con la misma factura. Sin este catch acá
    /// (en el repo, que es quien referencia Npgsql — Application NO referencia EF/Npgsql para
    /// mantener la capa desacoplada) la violación llegaría como DbUpdateException cruda y el
    /// endpoint respondería 500 en vez de 409.
    /// </summary>
    private static bool EsViolacionFacturaUnica(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
           && pg.ConstraintName == "IX_Gastos_ProveedorId_NumeroFactura";

    public Task ActualizarPagoAsync(PagoGasto pago)
    {
        _ctx.PagosGasto.Update(pago);
        return _ctx.SaveChangesAsync();
    }

    /// <inheritdoc/>
    /// FOR UPDATE serializa: dos pagos concurrentes sobre el mismo gasto se ejecutan uno
    /// detrás del otro (el segundo espera a que el primero haga commit/rollback), así que
    /// el SumAsync de pagos activos que lee el segundo YA ve el pago insertado por el
    /// primero. Mismo principio que MovimientoStockRepository.RegistrarMovimientoAtomicoAsync
    /// (guard atómico dentro de la transacción, no check-then-insert en memoria).
    public async Task<int> RegistrarPagoAtomicoAsync(PagoGasto pago)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var gasto = await _ctx.Gastos
            .FromSqlInterpolated($"SELECT * FROM \"Gastos\" WHERE \"Id\" = {pago.GastoId} FOR UPDATE")
            .FirstOrDefaultAsync();

        if (gasto is null)
        {
            await tx.RollbackAsync();
            throw new EntidadNoEncontradaException($"Gasto {pago.GastoId} no encontrado.");
        }
        if (!gasto.Activo)
        {
            await tx.RollbackAsync();
            throw new ReglaDeNegocioException("No se pueden registrar pagos sobre un gasto anulado.");
        }

        var totalPagado = await _ctx.PagosGasto
            .Where(p => p.GastoId == pago.GastoId && p.Activo)
            .SumAsync(p => (decimal?)p.Monto) ?? 0m;
        var saldoPendiente = gasto.MontoTotal - totalPagado;

        if (pago.Monto > saldoPendiente)
        {
            await tx.RollbackAsync();
            throw new ReglaDeNegocioException(
                $"El pago ({pago.Monto.ToString("C2", CulturaMonto)}) supera el saldo pendiente " +
                $"de la factura ({saldoPendiente.ToString("C2", CulturaMonto)}).");
        }

        _ctx.PagosGasto.Add(pago);
        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return pago.Id;
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

    public async Task<IReadOnlyList<PagoGasto>> ListarPagosActivosPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc)
        => await _ctx.PagosGasto
            .Include(p => p.Gasto!).ThenInclude(g => g!.Proveedor)
            .Include(p => p.Gasto!).ThenInclude(g => g!.RubroGasto)
            .Include(p => p.Gasto!).ThenInclude(g => g!.FuenteFinanciamiento)
            .Where(p => p.Activo && p.Gasto!.Activo && p.Fecha >= desdeUtc && p.Fecha <= hastaUtc)
            .OrderBy(p => p.Fecha)
            .ThenBy(p => p.Id)
            .ToListAsync();

    public async Task<decimal> TotalPagosActivosAntesDeAsync(DateTime fechaUtc)
        => await _ctx.PagosGasto
            .Where(p => p.Activo && p.Gasto!.Activo && p.Fecha < fechaUtc)
            .SumAsync(p => (decimal?)p.Monto) ?? 0m;

    public async Task<IReadOnlyList<Gasto>> ListarActivosConSaldoAsync()
        => await ConIncludes()
            .Where(g => g.Activo)
            .OrderBy(g => g.FechaVencimiento ?? g.Fecha)
            .ToListAsync();

    public async Task<IReadOnlyDictionary<int, decimal>> TotalGastadoPorLineaAsync(int ejercicio)
    {
        var filas = await _ctx.Gastos
            .Where(g => g.Activo && g.LineaPoaId != null && g.LineaPoa!.Ejercicio == ejercicio)
            .GroupBy(g => g.LineaPoaId!.Value)
            .Select(gr => new { LineaPoaId = gr.Key, Total = gr.Sum(g => g.MontoTotal) })
            .ToListAsync();

        return filas.ToDictionary(f => f.LineaPoaId, f => f.Total);
    }
}
