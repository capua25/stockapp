namespace StockApp.Domain.Enums;

/// <summary>
/// Ciclo de vida de un proyecto. A diferencia de EstadoTarea, Terminado y Cancelado no son
/// terminales: un proyecto cerrado puede reabrirse (vuelve a EnCurso).
/// </summary>
public enum EstadoProyecto
{
    Pendiente = 0,
    EnCurso = 1,
    Terminado = 2,
    Cancelado = 3,
}
