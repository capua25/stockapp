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
    /// ATÓMICO: los 3 cambios (insert movimiento + update StockActual + insert LogAuditoria)
    /// se stagean sobre el MISMO change tracker y se persisten en UN solo SaveChangesAsync.
    /// EF Core envuelve ese flush en una transacción implícita (BEGIN/COMMIT/ROLLBACK).
    public async Task<int> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
    {
        // 1) Producto trackeado por ESTE context (mismo change tracker que el flush)
        var producto = await _ctx.Productos.FindAsync(args.ProductoId)
            ?? throw new KeyNotFoundException($"Producto {args.ProductoId} no encontrado.");

        // 2) Stagear los 3 cambios sobre el mismo change tracker
        _ctx.MovimientosStock.Add(args.Movimiento);          // insert movimiento
        producto.StockActual = args.StockNuevo;              // update stock (entidad trackeada)
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RegistroMovimiento,   // 17
            Entidad   = "MovimientoStock",
            EntidadId = args.ProductoId,                     // referencia al producto (MovimientoId aún no existe)
            Detalle   = args.DetalleAuditoria
        });

        // 3) UN solo flush → transacción implícita atómica
        await _ctx.SaveChangesAsync();

        return args.Movimiento.Id;   // Id generado por la BD tras el flush
    }

    /// <inheritdoc/>
    public Task RecalcularAtomicoAsync(RecalculoAtomicoArgs args)
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
        => throw new NotImplementedException();
}
