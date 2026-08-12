using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Domain.Entities;

/// <summary>
/// Documento administrativo (expediente, oficio o suministro), spec 2026-08-11. Copia el
/// patrón de Tarea capa por capa: máquina de estados con diccionario privado
/// TransicionesValidas, CambiarEstado que valida y muta solo Estado, PuedeTransicionarA de
/// solo lectura para que la UI no recodifique las transiciones a mano. A diferencia de
/// Tarea, Finalizado y Anulado NO son terminales: admiten reapertura hacia EnProceso
/// (decisión 4), así que "cerrado" se parte en dos propiedades explícitas (EsActivo/
/// EsCerrado) en vez de derivarse de que TransicionesValidas[Estado] esté vacío.
/// FechaCierre no la toca CambiarEstado: la sella/limpia DocumentoAdministrativoService
/// (decisión 8), mismo criterio que Tarea.FechaFin no la toca Tarea.CambiarEstado.
/// </summary>
public class DocumentoAdministrativo
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public int Anio { get; set; }
    public TipoDocumento Tipo { get; set; }
    public DateTime FechaEmision { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Pendiente;

    public int RegistradoPorUsuarioId { get; set; }
    public Usuario? RegistradoPor { get; set; }
    public DateTime FechaRegistro { get; set; }
    public DateTime? FechaCierre { get; set; }

    public List<EventoDocumento> Eventos { get; set; } = new();

    private static readonly Dictionary<EstadoDocumento, EstadoDocumento[]> TransicionesValidas = new()
    {
        [EstadoDocumento.Pendiente]  = new[] { EstadoDocumento.EnProceso, EstadoDocumento.Anulado },
        [EstadoDocumento.EnProceso]  = new[] { EstadoDocumento.Pendiente, EstadoDocumento.Finalizado, EstadoDocumento.Anulado },
        [EstadoDocumento.Finalizado] = new[] { EstadoDocumento.EnProceso },
        [EstadoDocumento.Anulado]    = new[] { EstadoDocumento.EnProceso },
    };

    /// <summary>True si el trámite sigue en curso (Pendiente o EnProceso). Va a la solapa Activos.</summary>
    public bool EsActivo => Estado is EstadoDocumento.Pendiente or EstadoDocumento.EnProceso;

    /// <summary>True si el trámite está cerrado (Finalizado o Anulado). Va a la solapa Historial.</summary>
    public bool EsCerrado => Estado is EstadoDocumento.Finalizado or EstadoDocumento.Anulado;

    /// <summary>
    /// Valida y aplica la transición de estado (decisión 4 del spec). Rechaza cualquier
    /// combinación no listada en TransicionesValidas, incluida la identidad. No toca
    /// FechaCierre ni ningún otro campo: eso es responsabilidad del servicio (decisión 8).
    /// </summary>
    public void CambiarEstado(EstadoDocumento destino)
    {
        if (!PuedeTransicionarA(destino))
            throw new ReglaDeNegocioException(
                $"No se puede pasar el documento de '{Estado}' a '{destino}'.");
        Estado = destino;
    }

    /// <summary>
    /// Consulta de solo lectura sobre la misma tabla que usa CambiarEstado: única fuente de
    /// verdad de la máquina de estados. DocumentoFila (Presentation) debe consultar este
    /// método en vez de recodificar las transiciones a mano.
    /// </summary>
    public bool PuedeTransicionarA(EstadoDocumento destino) => TransicionesValidas[Estado].Contains(destino);

    /// <summary>
    /// Suma un evento al hilo append-only del documento (decisión 5 del spec). Única vía
    /// para agregar a Eventos — no hay método de borrado ni de edición en EventoDocumento.
    /// Fecha se sella acá (DateTime.UtcNow), igual que TareaService sella FechaFin: el
    /// llamador nunca pasa la fecha a mano.
    /// </summary>
    public void AgregarEvento(
        int usuarioId, string texto, bool esAutomatico,
        EstadoDocumento? anterior = null, EstadoDocumento? nuevo = null)
    {
        Eventos.Add(new EventoDocumento
        {
            DocumentoAdministrativoId = Id,
            Fecha = DateTime.UtcNow,
            UsuarioId = usuarioId,
            Texto = texto,
            EsAutomatico = esAutomatico,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
        });
    }
}
