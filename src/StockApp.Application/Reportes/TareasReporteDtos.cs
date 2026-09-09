namespace StockApp.Application.Reportes;

/// <summary>
/// Eje de agrupamiento del reporte-matriz de tareas (spec 2026-09-08, decisión 15): agrupar
/// por zona, dimensión, organismo, financiamiento o expediente es la misma pregunta con
/// distinto eje -- una sola pantalla en vez de cinco casi idénticas.
/// </summary>
public enum AgrupadorTareas
{
    Zona = 0,
    DimensionTematica = 1,
    OrganismoResponsable = 2,
    OrigenFinanciamiento = 3,
    Expediente = 4,
}

/// <summary>
/// Qué fecha de la tarea decide si cae dentro del rango del reporte (decisión 16). Con
/// <see cref="Cierre"/> el reporte cuenta ÚNICAMENTE tareas cerradas (Terminada/Cancelada):
/// FechaFin es null en Pendiente/EnCurso, así que esas columnas quedan en cero.
/// </summary>
public enum CriterioFechaTareas
{
    Creacion = 0,
    Cierre = 1,
}

/// <summary>
/// Filtro del reporte-matriz de tareas. Desde/Hasta son obligatorios (decisión 18):
/// se validan en <see cref="ReporteTareasService"/>, no solo con un default en la UI.
///
/// Contrato de <see cref="Desde"/>/<see cref="Hasta"/>: se esperan YA CONVERTIDAS A UTC (no
/// solo reetiquetadas). Quien las construya desde un control de calendario (instante local)
/// debe convertir con <c>DateTime.SpecifyKind(fechaLocal, DateTimeKind.Local).ToUniversalTime()</c>
/// -- NO con <c>SpecifyKind(fecha.Date, DateTimeKind.Utc)</c>, que es el criterio correcto para
/// un día-calendario sin componente horario (p. ej. <c>Tarea.FechaLimite</c>), pero acá filtra
/// un instante real (<c>FechaCreacion</c>/<c>FechaFin</c>, ambas timestamptz) y reetiquetar sin
/// convertir corre el rango 3hs respecto de lo que el usuario ve en pantalla (Uruguay UTC-3).
/// </summary>
public record FiltroReporteTareas(
    AgrupadorTareas Agrupador,
    CriterioFechaTareas Criterio,
    DateTime Desde,
    DateTime Hasta);

/// <summary>
/// Fila del reporte-matriz: un clasificador con sus conteos por estado. Pendientes + EnCurso +
/// Terminadas + Canceladas == Total siempre (decisión 17): Total es la cuenta completa del
/// grupo y esos cuatro son los únicos valores posibles de <c>EstadoTarea</c>.
/// </summary>
public record FilaReporteTareas(
    string Clasificador,
    int Pendientes,
    int EnCurso,
    int Terminadas,
    int Canceladas,
    int Total);

/// <summary>
/// DTO de matriz agnóstico de presentación (decisión 23): filas de clasificador con sus
/// conteos, sin nada atado a la grilla. Incorporar gráficos más adelante es escribir una vista
/// nueva que consuma este mismo DTO -- no toca Application, Api ni ApiClient.
/// </summary>
public record ReporteTareasDto(
    IReadOnlyList<FilaReporteTareas> Filas,
    int TotalGeneral);
