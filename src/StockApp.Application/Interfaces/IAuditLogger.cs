using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Abstracción para registrar eventos de auditoría en <c>LogAuditoria</c>.
/// La implementación concreta escribe a la BD vía EF Core.
/// </summary>
public interface IAuditLogger
{
    /// <summary>Registra un evento de auditoría de forma asincrónica.</summary>
    Task RegistrarAsync(
        int usuarioId,
        AccionAuditada accion,
        string entidad,
        int entidadId,
        string detalle);
}
