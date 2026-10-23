using StockApp.Domain.Enums;

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
    public Caja Caja { get; set; } = Caja.Local;           // caja que recibe el ingreso
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public decimal Monto { get; set; }                     // precisión 18,4
    public bool Activo { get; set; } = true;               // baja lógica

    /// <summary>Guid del lote de /confirmar que creó este ingreso (F5c). Null si es manual.</summary>
    // Esquema del importador ODS (eliminado 2026-10-21): inutilizado, se conserva para no generar drops. No borrar sin decisión.
    public Guid? IdImportacion { get; set; }
}
