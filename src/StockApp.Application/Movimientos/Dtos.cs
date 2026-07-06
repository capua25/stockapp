using StockApp.Domain.Enums;

namespace StockApp.Application.Movimientos;

/// <summary>Entrada del registro de movimiento.</summary>
public record RegistrarMovimientoDto(
    int ProductoId,
    TipoMovimiento Tipo,
    MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal? PrecioUnitario,   // null/0 permitido en Ajuste/Merma
    string? Comentario);

/// <summary>Resultado del registro exitoso (RM-12).</summary>
public record MovimientoRegistradoDto(
    int MovimientoId,
    int ProductoId,
    TipoMovimiento Tipo,
    MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal StockAnterior,
    decimal StockNuevo,
    DateTime Fecha);

/// <summary>Filtro de historial de movimientos (HM-02..HM-05).</summary>
public record HistorialMovimientoFiltro(
    int? ProductoId = null,
    TipoMovimiento? Tipo = null,
    DateTime? FechaDesde = null,
    DateTime? FechaHasta = null);

/// <summary>Ítem de historial con running balance (HM-07).</summary>
public record MovimientoHistorialDto(
    int MovimientoId,
    int ProductoId,
    string ProductoNombre,
    TipoMovimiento Tipo,
    MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal PrecioUnitario,
    decimal StockAnterior,
    decimal StockNuevo,
    string? Comentario,
    DateTime Fecha,
    int UsuarioId,
    string UsuarioNombre);

/// <summary>Resultado del recálculo de stock (RS-08).</summary>
public record RecalculoResultadoDto(
    int ProductoId,
    decimal StockAnterior,
    decimal StockNuevo,
    int TotalMovimientos);
