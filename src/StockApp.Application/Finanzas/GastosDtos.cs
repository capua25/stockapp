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
/// Resultado de alta/modificación de gasto. AdvertenciaSobregiro viene no-nula cuando la
/// línea POA queda sobregirada para la fuente del gasto: el spec §10 manda ADVERTIR pero
/// NO bloquear (la app avisa, el humano decide) — por eso es un dato del resultado y no
/// una excepción.
/// </summary>
public record ResultadoGastoDto(int Id, string? AdvertenciaSobregiro);
