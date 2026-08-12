namespace StockApp.Application.Documentos;

/// <summary>Metadatos de un adjunto de documento (sin bytes) — lo que devuelven los listados.</summary>
public record AdjuntoDocumentoDto(
    int Id, int DocumentoAdministrativoId, string NombreArchivo, string ContentType,
    long TamanoBytes, DateTime FechaAltaUtc);

/// <summary>Contenido completo para descarga (Results.File en el endpoint).</summary>
public record AdjuntoDocumentoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido);
