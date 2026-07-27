namespace StockApp.Application.Backups;

/// <summary>Fila de metadatos de una corrida de backup (spec §6, listado /backups). Resultado
/// viaja como string (ToString() del enum) — mismo criterio que FacturaCalendarioDto.Estado en
/// FinanzasVistasService, para no acoplar al desktop con el enum de Domain.</summary>
public sealed record CorridaBackupDto(
    int Id, DateTime FinalizadaEn, string Resultado, string? NombreArchivo, long? TamanioBytes, string? MotivoFallo);

/// <summary>Estado de salud del backup programado (spec §6 /backups/salud, spec §3 decisión 6
/// banner de InicioViewModel). Vencido = más de UmbralHoras sin una corrida exitosa (dos
/// ventanas de 12h + 2h de margen, para no disparar falsas alarmas por reinicios del servidor).
/// UmbralHoras viaja en el DTO (no se hardcodea el número en el texto del banner del desktop)
/// para que el umbral tenga una sola fuente de verdad: si cambia en ServicioConsultaBackups, el
/// texto que lee el admin en InicioViewModel no puede quedar mintiendo en silencio.</summary>
public sealed record SaludBackupDto(DateTime? UltimoExitoEn, bool Vencido, int UmbralHoras);

/// <summary>
/// Backup descargado del servidor: el contenido viaja como Stream (no byte[]) para no
/// bufferear un dump de varios MB/GB completo en memoria — GuardarBytesAsync lo copia directo
/// al Stream de escritura del archivo elegido por el usuario (spec §7). IAsyncDisposable:
/// liberar Contenido también libera la conexión HTTP subyacente.
/// </summary>
public sealed class BackupDescargaDto : IAsyncDisposable
{
    public string NombreArchivo { get; }
    public Stream Contenido { get; }

    public BackupDescargaDto(string nombreArchivo, Stream contenido)
    {
        NombreArchivo = nombreArchivo;
        Contenido = contenido;
    }

    public ValueTask DisposeAsync() => Contenido.DisposeAsync();
}
