namespace StockApp.Domain.Entities;

/// <summary>
/// Metadatos de un archivo adjunto a un Gasto (factura) o a un PagoGasto (recibo).
/// El contenido (bytes) vive SEPARADO en <see cref="AdjuntoContenido"/> (relación 1:1,
/// Id = AdjuntoId) para que listar adjuntos nunca arrastre los bytes de la BD.
/// Baja lógica con Activo, sin cascada: anular el Gasto/PagoGasto dueño NO anula sus
/// adjuntos (spec F3, decisión de alcance).
/// </summary>
public class Adjunto
{
    public int Id { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }

    /// <summary>Exactamente uno de GastoId/PagoGastoId es no nulo (invariante XOR).</summary>
    public int? GastoId { get; set; }
    public Gasto? Gasto { get; set; }

    public int? PagoGastoId { get; set; }
    public PagoGasto? PagoGasto { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaAltaUtc { get; set; }

    public bool EsDeGasto => GastoId is not null;
    public bool EsDePago => PagoGastoId is not null;
}
