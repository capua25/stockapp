namespace StockApp.Application.Tareas;

/// <summary>
/// Datos de clasificación de una tarea (spec 2026-09-08, D21): REEMPLAZO TOTAL de los cinco
/// campos en cada llamada a ITareaService.ReclasificarAsync — null en un campo significa
/// desasignar explícitamente ese clasificador, no "no tocar". Esta semántica es la que
/// permite que el modal de reclasificación (Presentation, Task 11) precargado funcione sin
/// ambigüedad: lo que el Admin ve en el modal es exactamente lo que queda guardado si no
/// toca nada.
/// </summary>
public record DatosClasificacionTarea(
    int? ZonaId,
    int? DimensionTematicaId,
    int? OrganismoResponsableId,
    int? OrigenFinanciamientoId,
    int? DocumentoAdministrativoId);
