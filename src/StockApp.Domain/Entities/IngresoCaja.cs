namespace StockApp.Domain.Entities;

/// <summary>
/// Ingreso de la caja municipal: partidas mensuales FIGM, multas, préstamos.
/// El saldo inicial del ejercicio entra como un ingreso "Saldo inicial".
/// </summary>
public class IngresoCaja
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }                    // UTC
    public string Concepto { get; set; } = string.Empty;   // obligatorio
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public decimal Monto { get; set; }                     // precisión 18,4
    public bool Activo { get; set; } = true;               // baja lógica
}
