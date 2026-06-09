namespace StockApp.Domain.Entities;

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
