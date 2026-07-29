using StockApp.Domain.Exceptions;

namespace StockApp.Application.Logs;

/// <summary>
/// Lee el directorio de logs del filesystem. Recibe la ruta por parametro (en vez de
/// inyectar el proveedor de rutas) siguiendo el mismo patron que
/// <c>ServicioConsultaBackups.ResolverArchivoParaDescargaAsync</c>: el endpoint resuelve
/// la ruta y este servicio solo opera sobre ella.
/// </summary>
public sealed class ServicioConsultaLogs
{
    private const string PatronArchivos = "*.log";

    public ResumenLogsDto ObtenerResumen(string directorioLogs)
    {
        var archivos = ListarArchivos(directorioLogs);
        if (archivos.Count == 0) return new ResumenLogsDto(0, null, null, 0);

        var infos = archivos.Select(r => new FileInfo(r)).ToList();
        return new ResumenLogsDto(
            infos.Count,
            infos.Min(i => i.LastWriteTime),
            infos.Max(i => i.LastWriteTime),
            infos.Sum(i => i.Length));
    }

    /// <summary>
    /// Devuelve las rutas completas a comprimir, ordenadas por nombre. Si no hay ningun
    /// archivo lanza <see cref="EntidadNoEncontradaException"/> — que el handler traduce a
    /// 404 — en vez de devolver un ZIP vacio que parezca un archivo corrupto.
    /// </summary>
    public IReadOnlyList<string> ResolverArchivosParaZip(string directorioLogs)
    {
        var archivos = ListarArchivos(directorioLogs);
        if (archivos.Count == 0)
            throw new EntidadNoEncontradaException(
                "No hay archivos de log para descargar todavía.");

        return archivos;
    }

    private static List<string> ListarArchivos(string directorioLogs)
    {
        if (string.IsNullOrWhiteSpace(directorioLogs) || !Directory.Exists(directorioLogs))
            return [];

        return Directory.GetFiles(directorioLogs, PatronArchivos)
            .OrderBy(r => Path.GetFileName(r), StringComparer.Ordinal)
            .ToList();
    }
}
