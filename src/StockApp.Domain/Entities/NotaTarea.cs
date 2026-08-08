namespace StockApp.Domain.Entities;

/// <summary>
/// Nota de una tarea, append-only (decisión 12 del spec): se agregan, nunca se editan ni
/// se borran — no hay métodos para eso ni en el dominio ni en el servicio. Sin nav de vuelta
/// a Tarea (mismo criterio que AsignacionPresupuestal → LineaPoa): la relación se configura
/// solo del lado padre en AppDbContext.
/// </summary>
public class NotaTarea
{
    public int Id { get; set; }
    public int TareaId { get; set; }
    public int UsuarioId { get; set; }

    /// <summary>Nav de solo lectura sobre UsuarioId, mismo criterio que MovimientoStock.Usuario
    /// / LogAuditoria.Usuario (fix/integridad-referencial): a diferencia de la relación con
    /// Tarea (child→parent del mismo agregado, sin nav a propósito), esta es una referencia de
    /// actor y el resto del código siempre le pone navegación.</summary>
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; }
    public string Texto { get; set; } = string.Empty;

    /// <summary>true si la generó el sistema (cambio de prioridad, acción sobre tarea ajena).</summary>
    public bool EsAutomatica { get; set; }
}
