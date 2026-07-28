using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Metadato de una corrida del backup programado (spec Backups §4.1). Los BYTES del dump
/// nunca entran a la base (guardar el respaldo de la base dentro de la base es circular) —
/// solo se persiste el nombre de archivo, y el archivo real vive en
/// IUserDataPathProvider.GetBackupsDirectory() del servidor.
/// </summary>
public class CorridaBackup
{
    public int Id { get; set; }
    public DateTime IniciadaEn { get; set; }              // UTC
    public DateTime FinalizadaEn { get; set; }             // UTC
    public ResultadoBackup Resultado { get; set; }

    /// <summary>Nombre del archivo .dump en GetBackupsDirectory(). Null si Resultado == Fallida.</summary>
    public string? NombreArchivo { get; set; }

    /// <summary>Tamaño del archivo generado. Null si Resultado == Fallida.</summary>
    public long? TamanioBytes { get; set; }

    /// <summary>
    /// stderr de pg_dump (o el motivo del fallo) cuando Resultado == Fallida.
    /// Deuda conocida (tercer review final E1, FIX MINOR): este campo cumple HOY dos roles
    /// distintos. Para una corrida Fallida real, es el motivo del fallo. Pero
    /// ServicioBackup.ReconciliarDumpHuerfanosAsync también lo usa -sin agregar una columna
    /// nueva, lo que exigiría una migración a esta altura de la entrega- para marcar una fila
    /// Exitosa reconstruida a partir de un .dump huérfano en disco (ver
    /// ServicioBackup.MarcaFilaReconciliada): en ese caso el campo no es null pese a que
    /// Resultado == Exitosa. Por eso ya NO es cierto que "Null si Resultado == Exitosa" -- ver
    /// FilaCorridaBackupVm.EsFallo / EsNotaInformativa en Presentation, que distinguen ambos
    /// casos para no pintar la nota informativa como un error.
    /// </summary>
    public string? MotivoFallo { get; set; }
}
