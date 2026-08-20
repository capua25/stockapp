using System;
using System.IO;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación de producción de <see cref="IRegistroFallos"/>: escribe a
/// %LocalAppData%/GestionMunicipal/logs/crash.log (mismo formato y ubicación que el
/// <c>Program.LogFatal</c> original, que ahora delega acá). Nunca debe tirar: si falla la
/// escritura, se traga la excepción silenciosamente para no enmascarar el error original que
/// se está intentando loguear.
///
/// Rotación simple (fix 2026-08-20, sin librería de logging): el archivo crecía sin techo
/// (4 MB / 5175 entradas en 3 semanas de uso real, sin ningún límite). Si al momento de
/// escribir el archivo ya supera <see cref="TamanioMaximoBytesPorDefecto"/>, se lo renombra a
/// "crash.log.1" (pisando la rotación anterior si existía) y se arranca un crash.log nuevo con
/// la entrada actual. Es deliberadamente el enfoque más chico que funciona: un solo nivel de
/// rotación (sin crash.log.2, .3, ...) alcanza para el volumen de este sistema (uso de un
/// municipio, no un servidor de alto tráfico) y evita traer una dependencia nueva para esto.
/// </summary>
public sealed class RegistroFallosArchivo : IRegistroFallos
{
    private const long TamanioMaximoBytesPorDefecto = 2 * 1024 * 1024; // 2 MB

    private readonly string _logPath;
    private readonly long _tamanioMaximoBytes;

    public RegistroFallosArchivo() : this(RutaPorDefecto(), TamanioMaximoBytesPorDefecto)
    {
    }

    /// <summary>Constructor para tests: ruta y umbral de rotación inyectados, sin tocar el
    /// %LocalAppData% real.</summary>
    internal RegistroFallosArchivo(string logPath, long tamanioMaximoBytes = TamanioMaximoBytesPorDefecto)
    {
        _logPath = logPath;
        _tamanioMaximoBytes = tamanioMaximoBytes;
    }

    private static string RutaPorDefecto() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMunicipal",
            "logs",
            "crash.log");

    public void LogFatal(string origen, Exception ex)
    {
        try
        {
            var logsDir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrEmpty(logsDir))
                Directory.CreateDirectory(logsDir);

            RotarSiExcedeElTamanioMaximo();

            var entrada =
                $"[{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fffzzz}] origen={origen} " +
                $"tipo={ex.GetType().FullName} mensaje={ex.Message}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}{Environment.NewLine}";

            File.AppendAllText(_logPath, entrada);
        }
        catch
        {
            // El logger nunca debe tirar: si falla la escritura, no hay nada más que hacer acá.
        }
    }

    private void RotarSiExcedeElTamanioMaximo()
    {
        var info = new FileInfo(_logPath);
        if (!info.Exists || info.Length <= _tamanioMaximoBytes)
            return;

        var rotado = _logPath + ".1";
        File.Copy(_logPath, rotado, overwrite: true);
        File.Delete(_logPath);
    }
}
