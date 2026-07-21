namespace StockApp.Domain.Entities;

/// <summary>
/// Línea de proyecto del POA (Rambla, Carpeta Asfáltica, Eventos, Prensa, ...).
/// Agregado: sus <see cref="AsignacionPresupuestal"/> (presupuesto por fuente de
/// financiamiento) se gestionan SIEMPRE a través de la línea, nunca sueltas.
/// </summary>
public class LineaPoa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;    // obligatorio, único por ejercicio
    public string Programa { get; set; } = string.Empty;  // obligatorio
    public int Ejercicio { get; set; }                    // año, ej. 2026
    public bool Activo { get; set; } = true;              // baja lógica

    /// <summary>Guid del lote de /confirmar que creó esta línea (F5c). Null si es manual.</summary>
    public Guid? IdImportacion { get; set; }

    public List<AsignacionPresupuestal> Asignaciones { get; set; } = new();
}
