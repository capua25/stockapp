using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class MovimientoStockRepository : IMovimientoStockRepository
{
    private readonly AppDbContext _ctx;

    public MovimientoStockRepository(AppDbContext ctx) => _ctx = ctx;

    /// <inheritdoc/>
    public Task<Producto?> ObtenerProductoAsync(int productoId)
        => _ctx.Productos.FindAsync(productoId).AsTask();

    /// <inheritdoc/>
    public async Task<(decimal Neto, int Total)> SumarMovimientosAsync(int productoId)
    {
        var movs = _ctx.MovimientosStock.Where(m => m.ProductoId == productoId);
        var entradas = await movs
            .Where(m => m.Tipo == TipoMovimiento.Entrada)
            .SumAsync(m => (decimal?)m.Cantidad) ?? 0m;
        var salidas = await movs
            .Where(m => m.Tipo == TipoMovimiento.Salida)
            .SumAsync(m => (decimal?)m.Cantidad) ?? 0m;
        var total = await movs.CountAsync();
        return (entradas - salidas, total);
    }

    /// <inheritdoc/>
    public Task<int> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task RecalcularAtomicoAsync(RecalculoAtomicoArgs args)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
        => throw new NotImplementedException();
}
