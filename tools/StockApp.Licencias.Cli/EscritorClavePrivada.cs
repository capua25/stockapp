namespace StockApp.Licencias.Cli;

/// <summary>Persiste la clave privada en disco sin pisar una existente por accidente. Sobreescribir
/// la clave privada en uso invalida TODAS las licencias ya emitidas con ella (dejan de validar,
/// 423 en cada instalación) y no hay forma de reemitirlas sin la clave original — por eso el
/// default es negarse, y solo con --forzar se permite, respaldando la anterior antes de escribir.</summary>
public static class EscritorClavePrivada
{
    public const string NombreArchivo = "clave-privada.pem";

    public static ResultadoEscritura Escribir(string directorioSalida, string privadaPem, bool forzar)
    {
        Directory.CreateDirectory(directorioSalida);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                directorioSalida,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var rutaPrivada = Path.Combine(directorioSalida, NombreArchivo);

        string? rutaRespaldo = null;
        if (File.Exists(rutaPrivada))
        {
            if (!forzar)
            {
                throw new InvalidOperationException(
                    $"Ya existe una clave privada en: {rutaPrivada}\n" +
                    "Sobreescribirla invalida TODAS las licencias ya emitidas con ella: dejan de " +
                    "validar (error 423 en cada instalación) y no hay forma de reemitirlas sin la " +
                    "clave original — es una pérdida irreversible.\n" +
                    "Si querés seguir usando la clave existente, no hace falta correr este comando.\n" +
                    "Si de verdad querés regenerarla, volvé a correr con --forzar (se respalda la " +
                    "clave anterior antes de sobreescribirla).");
            }

            rutaRespaldo = RutaRespaldoDisponible(rutaPrivada);
            File.Copy(rutaPrivada, rutaRespaldo);
        }

        File.WriteAllText(rutaPrivada, privadaPem);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(rutaPrivada, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return new ResultadoEscritura(rutaPrivada, rutaRespaldo);
    }

    private static string RutaRespaldoDisponible(string rutaPrivada)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var candidata = $"{rutaPrivada}.bak-{timestamp}";
        var sufijo = 1;
        while (File.Exists(candidata))
            candidata = $"{rutaPrivada}.bak-{timestamp}-{sufijo++}";
        return candidata;
    }
}

/// <summary>Resultado de <see cref="EscritorClavePrivada.Escribir"/>. <see cref="RutaRespaldo"/> es
/// null cuando no había clave previa que respaldar.</summary>
public sealed record ResultadoEscritura(string RutaPrivada, string? RutaRespaldo);
