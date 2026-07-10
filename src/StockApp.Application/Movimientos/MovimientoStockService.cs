using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Movimientos;

/// <summary>
/// Servicio de movimientos de stock.
/// Patrón: auth → validación → repo → retorno DTO.
/// La auditoría viaja dentro del repositorio atómico (no usa IAuditLogger).
/// </summary>
public class MovimientoStockService : IMovimientoStockService
{
    private readonly IMovimientoStockRepository _repo;
    private readonly ICurrentSession            _session;
    private readonly IAuthorizationService      _auth;

    public MovimientoStockService(
        IMovimientoStockRepository repo,
        ICurrentSession            session,
        IAuthorizationService      auth)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
    }

    // ── Registro ──────────────────────────────────────────────────────────────

    public async Task<MovimientoRegistradoDto> RegistrarAsync(
        RegistrarMovimientoDto dto, bool forzar = false)
    {
        // B3: autorización fail-closed (PRIMERO, antes de leer cualquier dato)
        _auth.Verificar(_session.RolActual, Permisos.RegistrarMovimientos);

        // B4: validaciones de dominio
        if (dto.Cantidad <= 0)
            throw new ArgumentException("La cantidad debe ser mayor que cero.", nameof(dto.Cantidad));

        ValidarTipoMotivo(dto.Tipo, dto.Motivo);

        if (dto.Motivo is MotivoMovimiento.Compra or MotivoMovimiento.Venta
            && (dto.PrecioUnitario is null or <= 0))
            throw new ArgumentException(
                $"El precio unitario es obligatorio y debe ser mayor que cero para el motivo '{dto.Motivo}'.",
                nameof(dto.PrecioUnitario));

        // B5: existencia y estado del producto
        var producto = await _repo.ObtenerProductoAsync(dto.ProductoId)
            ?? throw new EntidadNoEncontradaException($"Producto {dto.ProductoId} no encontrado.");

        if (!producto.Activo)
            throw new ReglaDeNegocioException(
                $"No se permiten movimientos sobre productos inactivos (ProductoId={dto.ProductoId}).");

        // B6: cálculo de signo (el guard de stock lo hace el UPDATE condicional en el repo)
        var stockAnterior = producto.StockActual;
        var delta         = dto.Tipo == TipoMovimiento.Entrada ? dto.Cantidad : -dto.Cantidad;
        var stockNuevo    = stockAnterior + delta;

        // B7: componer entidad + args + llamada atómica
        var movimiento = new MovimientoStock
        {
            ProductoId    = dto.ProductoId,
            UsuarioId     = _session.UsuarioActual!.Id,
            Tipo          = dto.Tipo,
            Motivo        = dto.Motivo,
            Cantidad      = dto.Cantidad,
            PrecioUnitario = dto.PrecioUnitario ?? 0m,
            Fecha         = DateTime.UtcNow,
            Comentario    = dto.Comentario
        };

        var detalle = $"ProductoId={dto.ProductoId}; Tipo={dto.Tipo}; Motivo={dto.Motivo}; " +
                      $"Cantidad={dto.Cantidad}; StockAnterior={stockAnterior}; StockNuevo={stockNuevo}";

        var args = new RegistroAtomicoArgs(
            Movimiento:       movimiento,
            ProductoId:       dto.ProductoId,
            Tipo:             dto.Tipo,
            Cantidad:         dto.Cantidad,
            Forzar:           forzar,
            UsuarioId:        _session.UsuarioActual!.Id,
            DetalleAuditoria: detalle);

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        // El guard "no negativo" lo hace la BD dentro de la transacción; acá se traduce
        // el resultado tipado a la excepción de dominio (respetando forzar, que ya viajó en args).
        if (resultado.Estado == ResultadoRegistroEstado.StockInsuficiente)
            throw new StockInsuficienteException(dto.ProductoId, resultado.StockResultante, dto.Cantidad);

        return new MovimientoRegistradoDto(
            MovimientoId:  resultado.MovimientoId,
            ProductoId:    dto.ProductoId,
            Tipo:          dto.Tipo,
            Motivo:        dto.Motivo,
            Cantidad:      dto.Cantidad,
            PrecioUnitario: dto.PrecioUnitario ?? 0m,
            StockAnterior: resultado.StockResultante - delta,
            StockNuevo:    resultado.StockResultante,
            Fecha:         movimiento.Fecha);
    }

    // ── Historial ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(
        HistorialMovimientoFiltro filtro)
    {
        // B3: fail-closed
        _auth.Verificar(_session.RolActual, Permisos.RegistrarMovimientos);

        return await _repo.ObtenerHistorialAsync(filtro);
    }

    // ── Recálculo ─────────────────────────────────────────────────────────────

    public async Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)
    {
        // B3: fail-closed
        _auth.Verificar(_session.RolActual, Permisos.RecalcularStock);

        var producto = await _repo.ObtenerProductoAsync(productoId)
            ?? throw new EntidadNoEncontradaException($"Producto {productoId} no encontrado.");

        var (neto, total) = await _repo.SumarMovimientosAsync(productoId);

        var stockAnterior = producto.StockActual;
        var stockNuevo    = neto; // Σ(Entrada) - Σ(Salida); 0 si sin movimientos

        var detalle = $"ProductoId={productoId}; StockAnterior={stockAnterior}; StockNuevo={stockNuevo}; " +
                      $"TotalMovimientos={total}";

        var args = new RecalculoAtomicoArgs(
            ProductoId:       productoId,
            StockNuevo:       stockNuevo,
            UsuarioId:        _session.UsuarioActual!.Id,
            DetalleAuditoria: detalle);

        await _repo.RecalcularAtomicoAsync(args);

        return new RecalculoResultadoDto(
            ProductoId:       productoId,
            StockAnterior:    stockAnterior,
            StockNuevo:       stockNuevo,
            TotalMovimientos: total);
    }

    // ── Validación privada Tipo×Motivo ────────────────────────────────────────

    /// <summary>
    /// Tabla validada (decisión firme #582):
    /// Compra  → Entrada; Venta → Salida; Merma → Salida; Ajuste → Entrada | Salida.
    /// </summary>
    private static void ValidarTipoMotivo(TipoMovimiento tipo, MotivoMovimiento motivo)
    {
        var valido = (tipo, motivo) switch
        {
            (TipoMovimiento.Entrada, MotivoMovimiento.Compra)  => true,
            (TipoMovimiento.Entrada, MotivoMovimiento.Ajuste)  => true,
            (TipoMovimiento.Salida,  MotivoMovimiento.Venta)   => true,
            (TipoMovimiento.Salida,  MotivoMovimiento.Merma)   => true,
            (TipoMovimiento.Salida,  MotivoMovimiento.Ajuste)  => true,
            _ => false
        };

        if (!valido)
            throw new ArgumentException(
                $"La combinación Tipo='{tipo}' con Motivo='{motivo}' no es válida. " +
                "Combinaciones permitidas: Entrada+{Compra,Ajuste}, Salida+{Venta,Merma,Ajuste}.");
    }
}
