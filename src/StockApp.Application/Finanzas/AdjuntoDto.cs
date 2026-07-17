namespace StockApp.Application.Finanzas;

/// <summary>Metadatos de un adjunto (sin bytes) — lo que devuelven los listados.</summary>
public record AdjuntoDto(
    int Id,
    string NombreArchivo,
    string ContentType,
    long TamanoBytes,
    int? GastoId,
    int? PagoGastoId,
    DateTime FechaAltaUtc);

/// <summary>Contenido completo para descarga (Results.File en el endpoint).</summary>
public record AdjuntoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido);
