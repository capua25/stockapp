namespace StockApp.Application.Finanzas;

/// <summary>
/// Paso de ANÁLISIS del importador (spec §8). Parsea las dos planillas, mapea cada fila a su
/// candidata de dominio con estado OK/advertencia/error, y concilia POA↔Gastos. READ-ONLY:
/// lee maestros para clasificar, NUNCA escribe. Exige el permiso ImportarPlanillas (solo Admin).
/// </summary>
public interface IAnalisisImportacionService
{
    Task<ResultadoAnalisisDto> AnalizarAsync(Stream planillaGastos, Stream planillaPoa, int ejercicio);
}
