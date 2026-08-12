using StockApp.Domain.Enums;

namespace StockApp.Application.Documentos;

/// <summary>
/// Filtro de listado de documentos administrativos, compartido por
/// DocumentoAdministrativoService y DocumentoApiClient (spec 2026-08-11) — mismo criterio
/// que GastoFiltro en Finanzas. Todos los campos son opcionales salvo en ListarHistorialAsync
/// (bloque de Application), que rechaza Anio nulo con ArgumentException (decisión 9).
/// </summary>
public record FiltroDocumentos(TipoDocumento? Tipo, int? Anio, string? Texto, EstadoDocumento? Estado);
