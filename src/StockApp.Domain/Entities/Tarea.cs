using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Domain.Entities;

/// <summary>
/// Tarea operativa del equipo (spec 2026-08-01). Módulo independiente: sin FK a otras
/// entidades del dominio (decisión 1). Lista común: se crea sin responsable, cualquiera
/// la toma (decisión 3). Sin baja lógica: Cancelada es un estado del ciclo de vida, no un
/// Activo=false (decisión 6). Guarda dos pares de trazabilidad independientes —
/// TomadaPor+FechaInicio (quién trabajó) y CerradaPor+FechaFin (quién cerró) — porque
/// cualquiera puede terminar o soltar una tarea ajena (decisión 11).
/// </summary>
public class Tarea
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public EstadoTarea Estado { get; set; } = EstadoTarea.Pendiente;
    public PrioridadTarea Prioridad { get; set; } = PrioridadTarea.Media;
    public DateTime? FechaLimite { get; set; }

    public int CreadaPorUsuarioId { get; set; }
    public DateTime FechaCreacion { get; set; }

    public int? TomadaPorUsuarioId { get; set; }

    /// <summary>Nav de solo lectura sobre TomadaPorUsuarioId: la grilla necesita el nombre
    /// del responsable actual (decisión 10) sin otra llamada — mismo criterio que
    /// Gasto.Proveedor para ProveedorNombre.</summary>
    public Usuario? TomadaPor { get; set; }
    public DateTime? FechaInicio { get; set; }

    public int? CerradaPorUsuarioId { get; set; }
    public DateTime? FechaFin { get; set; }

    public List<NotaTarea> Notas { get; set; } = new();

    private static readonly Dictionary<EstadoTarea, EstadoTarea[]> TransicionesValidas = new()
    {
        [EstadoTarea.Pendiente] = new[] { EstadoTarea.EnCurso, EstadoTarea.Cancelada },
        [EstadoTarea.EnCurso]   = new[] { EstadoTarea.Pendiente, EstadoTarea.Terminada, EstadoTarea.Cancelada },
        [EstadoTarea.Terminada] = Array.Empty<EstadoTarea>(),
        [EstadoTarea.Cancelada] = Array.Empty<EstadoTarea>(),
    };

    /// <summary>
    /// Valida y aplica la transición de estado (decisión 5 del spec). Rechaza cualquier
    /// combinación no listada en TransicionesValidas, incluida la identidad (ej.
    /// Pendiente→Pendiente): no existe una transición "sin cambios". Terminada y Cancelada
    /// no tienen salidas listadas: son terminales por construcción, no por un chequeo aparte.
    /// </summary>
    public void CambiarEstado(EstadoTarea destino)
    {
        if (!TransicionesValidas[Estado].Contains(destino))
            throw new ReglaDeNegocioException(
                $"No se puede pasar la tarea de '{Estado}' a '{destino}'.");
        Estado = destino;
    }
}
