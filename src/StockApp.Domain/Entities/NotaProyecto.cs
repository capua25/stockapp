namespace StockApp.Domain.Entities;

/// <summary>
/// Nota de un proyecto, append-only: se agregan, nunca se editan ni se borran. Mismo shape
/// que NotaTarea. Sin nav de vuelta a Proyecto: la relación se configura solo del lado padre.
/// </summary>
public class NotaProyecto
{
    public int Id { get; set; }
    public int ProyectoId { get; set; }
    public int UsuarioId { get; set; }

    /// <summary>Nav de solo lectura sobre UsuarioId (referencia de actor, igual que NotaTarea.Usuario).</summary>
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; }
    public string Texto { get; set; } = string.Empty;

    /// <summary>true si la generó el sistema (por ejemplo un cambio de estado).</summary>
    public bool EsAutomatica { get; set; }
}
