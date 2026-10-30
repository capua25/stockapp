using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Domain.Entities;

/// <summary>
/// Proyecto del municipio (spec 2026-10-30): agrupa tareas y gastos bajo un objetivo con
/// presupuesto opcional. Lleva los cuatro clasificadores (Zona, DimensionTematica,
/// OrganismoResponsable, OrigenFinanciamiento), todos opcionales. Sin baja lógica: Cancelado
/// es un estado del ciclo de vida. A diferencia de Tarea, un proyecto cerrado (Terminado o
/// Cancelado) puede reabrirse volviendo a EnCurso.
/// </summary>
public class Proyecto
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public EstadoProyecto Estado { get; set; } = EstadoProyecto.Pendiente;
    public PrioridadTarea Prioridad { get; set; } = PrioridadTarea.Media;
    public DateTime? FechaLimite { get; set; }

    /// <summary>Presupuesto del proyecto. Null = sin presupuesto (distinto de 0).</summary>
    public decimal? Presupuesto { get; set; }

    public int CreadoPorUsuarioId { get; set; }
    public DateTime FechaCreacion { get; set; }

    /// <summary>Sellada al entrar a un estado cerrado; null mientras el proyecto esté abierto.</summary>
    public DateTime? FechaFin { get; set; }

    public int? ZonaId { get; set; }
    public Zona? Zona { get; set; }
    public int? DimensionTematicaId { get; set; }
    public DimensionTematica? DimensionTematica { get; set; }
    public int? OrganismoResponsableId { get; set; }
    public OrganismoResponsable? OrganismoResponsable { get; set; }
    public int? OrigenFinanciamientoId { get; set; }
    public OrigenFinanciamiento? OrigenFinanciamiento { get; set; }

    public List<NotaProyecto> Notas { get; set; } = new();

    private static readonly Dictionary<EstadoProyecto, EstadoProyecto[]> TransicionesValidas = new()
    {
        [EstadoProyecto.Pendiente] = new[] { EstadoProyecto.EnCurso, EstadoProyecto.Cancelado },
        [EstadoProyecto.EnCurso]   = new[] { EstadoProyecto.Pendiente, EstadoProyecto.Terminado, EstadoProyecto.Cancelado },
        [EstadoProyecto.Terminado] = new[] { EstadoProyecto.EnCurso },
        [EstadoProyecto.Cancelado] = new[] { EstadoProyecto.EnCurso },
    };

    /// <summary>
    /// Valida y aplica la transición. Rechaza todo par no listado en TransicionesValidas, la
    /// identidad incluida. Sella FechaFin al entrar a un estado cerrado y la limpia al salir.
    /// </summary>
    public void CambiarEstado(EstadoProyecto destino, DateTime ahoraUtc)
    {
        if (!PuedeTransicionarA(destino))
            throw new ReglaDeNegocioException(
                $"No se puede pasar el proyecto de '{Estado}' a '{destino}'.");
        Estado = destino;
        FechaFin = EsCerrado ? ahoraUtc : null;
    }

    /// <summary>Consulta sobre la misma tabla que usa CambiarEstado: única fuente de verdad para la UI.</summary>
    public bool PuedeTransicionarA(EstadoProyecto destino) => TransicionesValidas[Estado].Contains(destino);

    /// <summary>True si el proyecto está Terminado o Cancelado (reabrible, a diferencia de Tarea.EsTerminal).</summary>
    public bool EsCerrado => Estado is EstadoProyecto.Terminado or EstadoProyecto.Cancelado;
}
