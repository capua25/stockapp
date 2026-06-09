namespace StockApp.Domain.Entities;

public class UnidadMedida
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;       // ej: Unidad, Metro, Kilo, Litro
    public string Abreviatura { get; set; } = string.Empty;  // ej: u, m, kg, l
    public bool Activo { get; set; } = true;                 // baja lógica (decisión 2026-06-09)
}
