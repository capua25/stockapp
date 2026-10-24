using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

/// <summary>Filtro combinable de la grilla "Gastos y facturas" (spec §7.1). Fechas en UTC.</summary>
public record GastoFiltro(
    DateTime? FechaDesde = null,
    DateTime? FechaHasta = null,
    int? ProveedorId = null,
    int? FuenteFinanciamientoId = null,
    int? RubroGastoId = null,
    int? LineaPoaId = null);

/// <summary>
/// Caja y literal (fuente de financiamiento) con que se paga un gasto de CONTADO: viven en su pago
/// automático, no en el gasto. Se eligen explícitamente; un gasto a crédito no la lleva (null), porque
/// cada pago de un crédito elige la suya.
/// </summary>
public record PagoContadoDto(Caja Caja, int FuenteFinanciamientoId);

/// <summary>
/// Resultado de alta/modificación de gasto. AdvertenciaSobregiro viene no-nula cuando la
/// línea POA queda sobregirada para el literal del pago de contado: el spec §10 manda ADVERTIR pero
/// NO bloquear (la app avisa, el humano decide) — por eso es un dato del resultado y no
/// una excepción.
/// </summary>
public record ResultadoGastoDto(int Id, string? AdvertenciaSobregiro);
