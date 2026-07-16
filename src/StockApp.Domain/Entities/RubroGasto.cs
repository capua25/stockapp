namespace StockApp.Domain.Entities;

/// <summary>
/// Rubro de gasto (los 17 rubros de la hoja Variables de la planilla).
/// El código numérico es el identificador de negocio con el que se importa/reporta.
/// </summary>
public class RubroGasto
{
    public int Id { get; set; }
    public int Codigo { get; set; }                       // obligatorio, único
    public string Nombre { get; set; } = string.Empty;    // obligatorio
    public bool Activo { get; set; } = true;              // baja lógica
}
