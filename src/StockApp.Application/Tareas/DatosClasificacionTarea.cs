namespace StockApp.Application.Tareas;

/// <summary>
/// Datos de clasificación de una tarea (spec 2026-09-08, D21; spec 2026-10-30): REEMPLAZO TOTAL
/// de los dos campos en cada llamada a ITareaService.ReclasificarAsync — null en un campo
/// significa desvincular explícitamente, no "no tocar". Esta semántica es la que permite que el
/// modal de reclasificación precargado funcione sin ambigüedad: lo que el Admin ve en el modal es
/// exactamente lo que queda guardado si no toca nada.
/// </summary>
public record DatosClasificacionTarea(int? ProyectoId, int? DocumentoAdministrativoId);
