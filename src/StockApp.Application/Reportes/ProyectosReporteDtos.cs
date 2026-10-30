namespace StockApp.Application.Reportes;

/// <summary>
/// Eje de agrupamiento del reporte-matriz de proyectos (spec 2026-09-08, decisión 15, retargeteado
/// a proyectos en el spec 2026-10-30): agrupar por zona, dimensión, organismo o financiamiento es
/// la misma pregunta con distinto eje -- una sola pantalla en vez de cuatro casi idénticas. El eje
/// Expediente se eliminó: la ficha de cada expediente ya lista sus tareas vinculadas.
/// </summary>
public enum AgrupadorProyectos
{
    Zona = 0,
    DimensionTematica = 1,
    OrganismoResponsable = 2,
    OrigenFinanciamiento = 3,
}

/// <summary>
/// Qué fecha del proyecto decide si cae dentro del rango del reporte (decisión 16). Con
/// <see cref="Cierre"/> el reporte cuenta ÚNICAMENTE proyectos cerrados (Terminado/Cancelado):
/// <c>Proyecto.FechaFin</c> es null en Pendiente/EnCurso, así que esas columnas quedan en cero.
/// </summary>
public enum CriterioFechaProyectos
{
    Creacion = 0,
    Cierre = 1,
}

/// <summary>
/// Filtro del reporte-matriz de proyectos. Desde/Hasta son obligatorios (decisión 18):
/// se validan en <see cref="ReporteProyectosService"/>, no solo con un default en la UI.
///
/// Contrato de <see cref="Desde"/>/<see cref="Hasta"/>: se esperan YA CONVERTIDAS A UTC (no
/// solo reetiquetadas). Quien las construya desde un control de calendario (instante local)
/// debe convertir con <c>DateTime.SpecifyKind(fechaLocal, DateTimeKind.Local).ToUniversalTime()</c>
/// -- NO con <c>SpecifyKind(fecha.Date, DateTimeKind.Utc)</c>, que es el criterio correcto para
/// un día-calendario sin componente horario (p. ej. <c>Proyecto.FechaLimite</c>), pero acá filtra
/// un instante real (<c>FechaCreacion</c>/<c>FechaFin</c>, ambas timestamptz) y reetiquetar sin
/// convertir corre el rango 3hs respecto de lo que el usuario ve en pantalla (Uruguay UTC-3).
/// </summary>
public record FiltroReporteProyectos(
    AgrupadorProyectos Agrupador,
    CriterioFechaProyectos Criterio,
    DateTime Desde,
    DateTime Hasta);

/// <summary>
/// Fila del reporte-matriz: un clasificador con sus conteos por estado. Pendientes + EnCurso +
/// Terminados + Cancelados == Total siempre (decisión 17): Total es la cuenta completa del
/// grupo y esos cuatro son los únicos valores posibles de <c>EstadoProyecto</c>.
/// </summary>
public record FilaReporteProyectos(
    string Clasificador,
    int Pendientes,
    int EnCurso,
    int Terminados,
    int Cancelados,
    int Total);

/// <summary>
/// DTO de matriz agnóstico de presentación (decisión 23): filas de clasificador con sus
/// conteos, sin nada atado a la grilla. <see cref="TotalGeneral"/> es la suma columna a columna
/// de <see cref="Filas"/> (su <c>Clasificador</c> es "Total general"), calculada en el servicio.
/// </summary>
public record ReporteProyectosDto(
    IReadOnlyList<FilaReporteProyectos> Filas,
    FilaReporteProyectos TotalGeneral);
