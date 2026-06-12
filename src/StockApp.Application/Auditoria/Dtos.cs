using StockApp.Domain.Enums;

namespace StockApp.Application.Auditoria;

/// <summary>Item del historial de auditoría.</summary>
public record AuditoriaItemDto(
    DateTime Fecha,
    string NombreUsuario,
    AccionAuditada Accion,
    string Entidad,
    int EntidadId,
    string Detalle);
