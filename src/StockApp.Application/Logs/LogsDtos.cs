namespace StockApp.Application.Logs;

/// <summary>
/// Metadatos agregados del directorio de logs. No expone nombres de archivo individuales:
/// la descarga es siempre el ZIP completo, asi no hay ningun parametro de nombre de
/// archivo que pueda convertirse en superficie de path traversal.
/// </summary>
public sealed record ResumenLogsDto(
    int CantidadArchivos, DateTime? DesdeFecha, DateTime? HastaFecha, long TamanioTotalBytes);

public sealed class LogsDescargaDto : IAsyncDisposable
{
    public string NombreArchivo { get; }
    public Stream Contenido { get; }

    public LogsDescargaDto(string nombreArchivo, Stream contenido)
    {
        NombreArchivo = nombreArchivo;
        Contenido = contenido;
    }

    public ValueTask DisposeAsync() => Contenido.DisposeAsync();
}
