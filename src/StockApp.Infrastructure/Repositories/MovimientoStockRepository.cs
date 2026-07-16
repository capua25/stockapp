using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
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
    /// ATÓMICO: transacción explícita que envuelve un UPDATE CONDICIONAL de stock
    /// (la base serializa la fila y hace cumplir "no negativo"), el insert del movimiento
    /// y el insert del LogAuditoria. Para salidas sin forzar, 0 filas afectadas ⇒ StockInsuficiente
    /// (rollback, no se inserta nada). Entradas y salidas forzadas aplican el delta sin guard.
    public virtual async Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        if (args.Tipo == TipoMovimiento.Salida && !args.Forzar)
        {
            // UPDATE condicional atómico: solo baja si hay stock suficiente
            var filas = await _ctx.Productos
                .Where(p => p.Id == args.ProductoId && p.StockActual >= args.Cantidad)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual - args.Cantidad));

            if (filas == 0)
            {
                // 0 filas: stock insuficiente O producto inexistente. Distinguir (el producto
                // usa baja lógica, así que la fila persiste; 0 filas normalmente = insuficiente).
                var stockActual = await _ctx.Productos
                    .Where(p => p.Id == args.ProductoId)
                    .Select(p => (decimal?)p.StockActual)
                    .FirstOrDefaultAsync();

                if (stockActual is null)
                    throw new EntidadNoEncontradaException($"Producto {args.ProductoId} no encontrado.");

                await tx.RollbackAsync();
                return new ResultadoRegistro(ResultadoRegistroEstado.StockInsuficiente, 0, stockActual.Value);
            }
        }
        else
        {
            // Entrada, o salida forzada (permite negativo): delta con signo, sin guard
            var delta = args.Tipo == TipoMovimiento.Entrada ? args.Cantidad : -args.Cantidad;
            var filas = await _ctx.Productos
                .Where(p => p.Id == args.ProductoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual + delta));

            if (filas == 0)
                throw new EntidadNoEncontradaException($"Producto {args.ProductoId} no encontrado.");
        }

        // Insert movimiento + log dentro de la MISMA transacción
        _ctx.MovimientosStock.Add(args.Movimiento);
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RegistroMovimiento,   // 17
            Entidad   = "MovimientoStock",
            EntidadId = args.ProductoId,
            Detalle   = args.DetalleAuditoria
        });
        await _ctx.SaveChangesAsync();

        await tx.CommitAsync();

        // Stock resultante autoritativo (proyección escalar → lee de la BD, no del change tracker)
        var stockResultante = await _ctx.Productos
            .Where(p => p.Id == args.ProductoId)
            .Select(p => p.StockActual)
            .FirstAsync();

        return new ResultadoRegistro(ResultadoRegistroEstado.Ok, args.Movimiento.Id, stockResultante);
    }

    /// <inheritdoc/>
    /// ATÓMICO: update Producto.StockActual + insert LogAuditoria (Accion=18)
    /// en UN solo SaveChangesAsync. Mismo principio que RegistrarMovimientoAtomicoAsync.
    public async Task RecalcularAtomicoAsync(RecalculoAtomicoArgs args)
    {
        var producto = await _ctx.Productos.FindAsync(args.ProductoId)
            ?? throw new EntidadNoEncontradaException($"Producto {args.ProductoId} no encontrado.");

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
    /// acumulando en orden ASC sobre la secuencia COMPLETA del producto.
    ///
    /// BUG-03: Tipo/FechaDesde/FechaHasta ya NO se aplican en la query (a nivel BD): el
    /// running balance necesita la secuencia completa de cada producto para ser correcto,
    /// filtrar antes hacia que arrancara en 0 sobre el subconjunto filtrado (repro real:
    /// filtro Tipo=Entrada mostraba stock "10 -> 20" en vez del real "-6 -> 4"). Solo
    /// ProductoId se aplica ANTES, porque el balance es independiente por producto.
    /// Trade-off de performance: sin ProductoId, trae TODOS los movimientos de la tabla en
    /// vez de un subconjunto ya filtrado por Tipo/fecha. Se prioriza correccion; si el
    /// volumen crece, evaluar precalcular el balance en la BD (ventana SUM) en vez de en
    /// memoria.
    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
    {
        // Construir query con filtros condicionales encadenados (patrón ProductoRepository.BuscarAsync)
        var q = _ctx.MovimientosStock
            .Include(m => m.Producto)
            .Include(m => m.Usuario)
            .AsQueryable();

        if (filtro.ProductoId.HasValue)
            q = q.Where(m => m.ProductoId == filtro.ProductoId.Value);

        // Traer ordenado ASC para calcular running balance correctamente, sobre la secuencia
        // completa (Tipo/fechas se aplican DESPUES del calculo, ver comentario abajo).
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
                UsuarioId:      m.UsuarioId,
                UsuarioNombre:  m.Usuario?.NombreCompleto ?? m.Usuario?.NombreUsuario ?? string.Empty
            ));
        }

        // Filtros de Tipo/fechas AHORA, sobre el resultado YA anotado con el balance real
        // (BUG-03: aplicarlos antes hacia que el balance arrancara en 0 sobre el subconjunto).
        IEnumerable<MovimientoHistorialDto> filtrados = items;

        if (filtro.Tipo.HasValue)
            filtrados = filtrados.Where(i => i.Tipo == filtro.Tipo.Value);

        if (filtro.FechaDesde.HasValue)
            filtrados = filtrados.Where(i => i.Fecha >= filtro.FechaDesde.Value);

        if (filtro.FechaHasta.HasValue)
        {
            // HM-04: FechaHasta sin hora se normaliza a fin del dia (23:59:59.9999999).
            var fechaHastaFinDia = filtro.FechaHasta.Value.Date.AddDays(1).AddTicks(-1);
            filtrados = filtrados.Where(i => i.Fecha <= fechaHastaFinDia);
        }

        // Orden final DESC por Fecha GLOBAL (HM-S07), no por ProductoId.
        return filtrados
            .OrderByDescending(i => i.Fecha)
            .ThenByDescending(i => i.MovimientoId)
            .ToList();
    }
}
