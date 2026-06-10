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
    public Task<(decimal Neto, int Total)> SumarMovimientosAsync(int productoId)
        => throw new NotImplementedException();

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
