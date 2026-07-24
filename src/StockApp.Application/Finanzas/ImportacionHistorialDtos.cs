namespace StockApp.Application.Finanzas;

/// <summary>
/// Fila del historial de importaciones (F5d §3). Se deriva ENTERAMENTE de LogsAuditoria — sin
/// entidad cabecera ni migración nueva. Revertida se calcula comparando IdLote contra los logs
/// de AccionAuditada.ReversionImportacion (mismo patrón que
/// ImportacionRepository.BuscarImportacionNoRevertidaAsync).
/// </summary>
public sealed record ImportacionHistorialDto(
    Guid IdImportacion, DateTime Fecha, int Ejercicio, string Usuario, bool Revertida);
