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
    /// ATÓMICO: update Producto.StockActual + insert LogAuditoria (Accion=18)
    /// en UN solo SaveChangesAsync. Mismo principio que RegistrarMovimientoAtomicoAsync.
    public async Task RecalcularAtomicoAsync(RecalculoAtomicoArgs args)
    {
        var producto = await _ctx.Productos.FindAsync(args.ProductoId)
            ?? throw new KeyNotFoundException($"Producto {args.ProductoId} no encontrado.");

        producto.StockActual = args.StockNuevo;

        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RecalculoStock,   // 18
            Entidad   = "Producto",
            EntidadId = args.ProductoId,
            Detalle   = args.DetalleAuditoria
        });

        await _ctx.SaveChangesAsync();
    }

    /// <inheritdoc/>
    /// Filtros combinables con AND; OrderByDescending(Fecha).
    /// Running balance calculado en memoria (StockAnterior/StockNuevo) por producto,
    /// acumulando en orden ASC y luego invirtiendo para la vista DESC.
    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
    {
        // Construir query con filtros condicionales encadenados (patrón ProductoRepository.BuscarAsync)
        var q = _ctx.MovimientosStock
            .Include(m => m.Producto)
            .AsQueryable();

        if (filtro.ProductoId.HasValue)
            q = q.Where(m => m.ProductoId == filtro.ProductoId.Value);

        if (filtro.Tipo.HasValue)
            q = q.Where(m => m.Tipo == filtro.Tipo.Value);

        if (filtro.FechaDesde.HasValue)
            q = q.Where(m => m.Fecha >= filtro.FechaDesde.Value);

        if (filtro.FechaHasta.HasValue)
        {
            // HM-04: FechaHasta sin hora se normaliza a fin del día (23:59:59.9999999).
            // Usamos .Date.AddDays(1).AddTicks(-1) para cubrir el día completo,
            // independientemente de si el usuario pasó medianoche u otra hora.
            var fechaHastaFinDia = filtro.FechaHasta.Value.Date.AddDays(1).AddTicks(-1);
            q = q.Where(m => m.Fecha <= fechaHastaFinDia);
        }

        // Traer ordenado ASC para calcular running balance correctamente
        var movimientos = await q
            .OrderBy(m => m.ProductoId)
            .ThenBy(m => m.Fecha)
            .ThenBy(m => m.Id)
            .ToListAsync();

        if (movimientos.Count == 0)
            return Array.Empty<MovimientoHistorialDto>();

        // Running balance por producto (acumulación ASC)
        var acumulado = new Dictionary<int, decimal>();
        var items = new List<MovimientoHistorialDto>(movimientos.Count);

        foreach (var m in movimientos)
        {
            if (!acumulado.ContainsKey(m.ProductoId))
                acumulado[m.ProductoId] = 0m;

            var stockAnterior = acumulado[m.ProductoId];
            var delta         = m.Tipo == TipoMovimiento.Entrada ? m.Cantidad : -m.Cantidad;
            var stockNuevo    = stockAnterior + delta;
            acumulado[m.ProductoId] = stockNuevo;

            items.Add(new MovimientoHistorialDto(
                MovimientoId:   m.Id,
                ProductoId:     m.ProductoId,
                ProductoNombre: m.Producto?.Nombre ?? string.Empty,
                Tipo:           m.Tipo,
                Motivo:         m.Motivo,
                Cantidad:       m.Cantidad,
                PrecioUnitario: m.PrecioUnitario,
                StockAnterior:  stockAnterior,
                StockNuevo:     stockNuevo,
                Comentario:     m.Comentario,
                Fecha:          m.Fecha,
                UsuarioId:      m.UsuarioId
            ));
        }

        // Invertir a DESC para la vista (HM-S07: OrderByDescending(Fecha))
        items.Reverse();
        return items;
    }
}
