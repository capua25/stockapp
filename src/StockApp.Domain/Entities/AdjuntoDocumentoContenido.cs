namespace StockApp.Domain.Entities;

/// <summary>
/// Bytes del adjunto de un documento administrativo, en tabla propia (mapea a bytea en
/// Postgres). Id comparte valor con el AdjuntoDocumento dueño (relación 1:1 configurada en
/// AppDbContext) — mismo patrón exacto que AdjuntoContenido en Finanzas.
/// </summary>
public class AdjuntoDocumentoContenido
{
    public int Id { get; set; }
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
}
