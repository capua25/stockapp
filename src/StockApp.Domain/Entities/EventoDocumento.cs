using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Evento del hilo de historial de un DocumentoAdministrativo, append-only (decisión 5 del
/// spec, molde de NotaTarea): se agregan vía DocumentoAdministrativo.AgregarEvento, nunca
/// se editan ni se borran. EstadoAnterior/EstadoNuevo van completos cuando EsAutomatico
/// viene de un cambio de estado (CambioEstadoDocumento) y quedan nulos para notas manuales
/// y para altas/bajas de adjunto automáticas (decisión 11d) — un evento automático sin
/// cambio de estado es tan válido como uno con cambio de estado.
/// </summary>
public class EventoDocumento
{
    public int Id { get; set; }
    public int DocumentoAdministrativoId { get; set; }
    public DateTime Fecha { get; set; }
    public int UsuarioId { get; set; }

    /// <summary>Nav de solo lectura sobre UsuarioId, mismo criterio que NotaTarea.Usuario: el
    /// hilo de eventos necesita mostrar quién generó cada entrada sin una consulta aparte.</summary>
    public Usuario? Usuario { get; set; }

    public EstadoDocumento? EstadoAnterior { get; set; }
    public EstadoDocumento? EstadoNuevo { get; set; }
    public string Texto { get; set; } = string.Empty;

    /// <summary>true si lo generó el sistema (cambio de estado, adjunto agregado/quitado);
    /// false para una nota manual del funcionario.</summary>
    public bool EsAutomatico { get; set; }
}
