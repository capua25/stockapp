namespace StockApp.Domain.Entities;

/// <summary>
/// Metadatos de un archivo adjunto a un DocumentoAdministrativo (decisión 10 del spec).
/// Entidad propia, NO reusa Adjunto de Finanzas: Documentos es un módulo independiente y
/// no debe meterse dentro del CHECK XOR de Finanzas. El contenido (bytes) vive SEPARADO en
/// AdjuntoDocumentoContenido (relación 1:1, Id = AdjuntoDocumentoId) para que listar
/// adjuntos nunca arrastre bytes de la BD — mismo patrón que Adjunto/AdjuntoContenido.
/// Baja lógica con Activo, nunca borrado físico (decisión 11c).
/// </summary>
public class AdjuntoDocumento
{
    public int Id { get; set; }
    public int DocumentoAdministrativoId { get; set; }
    public DocumentoAdministrativo? Documento { get; set; }

    public string NombreArchivo { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaAltaUtc { get; set; }
}
