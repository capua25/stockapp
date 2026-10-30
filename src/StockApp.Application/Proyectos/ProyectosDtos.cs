using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Proyectos;

/// <summary>Datos editables de un proyecto (alta y modificación). Estado y creador no viajan: los fija el servicio.</summary>
public record DatosProyecto(
    string Titulo, string? Descripcion, PrioridadTarea Prioridad,
    DateTime? FechaLimite, decimal? Presupuesto,
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId, int? OrigenFinanciamientoId);

/// <summary>Gasto activo imputado a un proyecto, tal como se muestra en su ficha.</summary>
public record GastoDeProyectoDto(int Id, DateTime Fecha, string ProveedorNombre, string Detalle, decimal MontoTotal);

/// <summary>Ficha de un proyecto con su ejecución presupuestaria.</summary>
/// <param name="TotalGastado">Suma de los gastos activos imputados al proyecto.</param>
/// <param name="Disponible">Presupuesto - TotalGastado; null si el proyecto no tiene presupuesto.</param>
/// <param name="Gastos">Null si la sesión no tiene finanzas.ver.</param>
public record FichaProyectoDto(
    Proyecto Proyecto, decimal TotalGastado, decimal? Disponible,
    IReadOnlyList<GastoDeProyectoDto>? Gastos);

/// <summary>Opción mínima para los selectores de proyecto de gasto, factura y tarea.</summary>
public record OpcionProyectoDto(int Id, string Titulo, EstadoProyecto Estado);
