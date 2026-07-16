namespace StockApp.Domain.Entities;

/// <summary>
/// Fuente de financiamiento ("literal" FIGM: A, B, C, Multas, Excedentes/Préstamos).
/// Maestro cerrado del módulo Finanzas — hoy texto libre en la planilla de gastos.
/// </summary>
public class FuenteFinanciamiento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
