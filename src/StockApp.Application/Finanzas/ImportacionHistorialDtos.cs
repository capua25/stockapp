namespace StockApp.Application.Finanzas;

/// <summary>
/// Fila del historial de importaciones (F5d §3). fix/integridad-referencial: se lee de
/// LoteImportacion — Revertida es RevertidaEn != null, un campo real de esa fila (antes se
/// derivaba comparando IdLote contra los logs de AccionAuditada.ReversionImportacion en
/// LogsAuditoria). El contrato del DTO no cambió.
/// </summary>
public sealed record ImportacionHistorialDto(
    Guid IdImportacion, DateTime Fecha, int Ejercicio, string Usuario, bool Revertida);
