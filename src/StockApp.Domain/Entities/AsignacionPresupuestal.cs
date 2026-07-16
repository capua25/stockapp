namespace StockApp.Domain.Entities;

/// <summary>
/// Presupuesto de una línea POA POR fuente de financiamiento — resuelve el
/// financiamiento mixto B+C (caso real COMPOSTERAS). Hija del agregado LineaPoa:
/// sin Activo propio; modificar la línea reemplaza su lista completa de asignaciones.
/// </summary>
public class AsignacionPresupuestal
{
    public int Id { get; set; }
    public int LineaPoaId { get; set; }
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public decimal Monto { get; set; }  // precisión 18,4
}
