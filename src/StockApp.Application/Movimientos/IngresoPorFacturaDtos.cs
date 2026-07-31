using StockApp.Domain.Enums;

namespace StockApp.Application.Movimientos;

/// <summary>Cabecera + renglones de una factura de compra a ingresar en un solo lote atómico.</summary>
public record IngresoPorFacturaDto(
    int ProveedorId,
    string? NumeroFactura,
    string? NumeroOrden,
    DateTime Fecha,
    string Detalle,
    string? Destino,
    decimal MontoTotal,
    int FuenteFinanciamientoId,
    int RubroGastoId,
    int? LineaPoaId,
    CondicionPago CondicionPago,
    DateTime? FechaVencimiento,
    IReadOnlyList<RenglonFacturaDto> Renglones);

/// <summary>Renglón de artículo. Exactamente uno de ProductoId/ProductoNuevo debe venir seteado.</summary>
public record RenglonFacturaDto(
    int? ProductoId,
    ProductoNuevoDto? ProductoNuevo,
    decimal Cantidad,
    decimal PrecioUnitario,
    bool ActualizarPrecioCosto);

/// <summary>Datos del alta en línea de un producto nuevo dentro del lote.</summary>
public record ProductoNuevoDto(
    string Codigo,
    string Nombre,
    int? CategoriaId,
    int UnidadMedidaId,
    decimal PrecioVenta);

/// <summary>Resultado del alta: id del gasto, ids de movimiento generados y totales calculados.</summary>
public record IngresoPorFacturaResultadoDto(
    int GastoId,
    IReadOnlyList<int> MovimientoIds,
    decimal SumaRenglones,
    decimal DiferenciaConTotal);
