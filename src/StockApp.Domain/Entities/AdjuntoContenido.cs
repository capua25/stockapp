namespace StockApp.Domain.Entities;

/// <summary>
/// Bytes del adjunto, en tabla propia (mapea a <c>bytea</c> en Postgres). Id comparte
/// valor con el <see cref="Adjunto"/> dueño (relación 1:1 configurada en AppDbContext) —
/// separarlo del registro de metadatos evita que los listados de adjuntos (grilla del
/// formulario) traigan bytes que nadie pidió.
/// </summary>
public class AdjuntoContenido
{
    public int Id { get; set; }
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
}
