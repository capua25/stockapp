using System.Diagnostics;

namespace StockApp.Tests.Compartido;

/// <summary>
/// Corre un snippet de bash y devuelve su salida. Existe para poder testear la librería de
/// shell del kit de instalación (deploy/kit/lib/) desde la MISMA suite de xUnit que todo lo
/// demás, en vez de agregar un segundo runner (bats) con su propia historia de CI.
///
/// Nada de esto se saltea en silencio si falta bash: un guardián que se auto-desactiva es un
/// guardián que se pudre sin que nadie se entere. Este proyecto corre sobre Linux/WSL2 (ver
/// decisión 1 del diseño 2026-09-10: el servidor es Linux), así que la ausencia de bash es un
/// entorno roto, no un caso a tolerar. xUnit v2 (2.5.3, ver Directory.Packages.props) no tiene
/// Assert.Skip, así que tampoco habría forma limpia de saltear.
/// </summary>
internal static class EjecutorBash
{
    internal static (int ExitCode, string Stdout, string Stderr) Ejecutar(string script)
    {
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException(
                "Estos tests verifican la librería bash del kit de instalación (servidor Linux) " +
                "y necesitan correr en Linux. Corré la suite desde WSL2.");

        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proceso.StartInfo.ArgumentList.Add("-c");
        proceso.StartInfo.ArgumentList.Add(script);

        proceso.Start();
        var stdout = proceso.StandardOutput.ReadToEnd();
        var stderr = proceso.StandardError.ReadToEnd();
        proceso.WaitForExit();

        return (proceso.ExitCode, stdout.TrimEnd('\n'), stderr.TrimEnd('\n'));
    }

    /// <summary>Raíz del repo, derivada del path de compilación de este archivo.</summary>
    internal static string RaizDelRepo(string archivoDeEsteTest)
    {
        var dir = Path.GetDirectoryName(archivoDeEsteTest)!;   // tests/_Compartido
        return Path.GetFullPath(Path.Combine(dir, "..", ".."));
    }
}
