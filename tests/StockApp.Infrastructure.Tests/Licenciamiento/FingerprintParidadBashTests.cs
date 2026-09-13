using System.Runtime.CompilerServices;
using StockApp.Infrastructure.Licenciamiento;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Licenciamiento;

/// <summary>
/// Ata el snippet bash de deploy/kit/lib/fingerprint.sh (el que usa 00-preflight.sh para
/// imprimir el código de máquina ANTES de instalar nada) con FingerprintMaquinaBase, que es
/// lo que la API usa para validar la licencia.
///
/// POR QUÉ existe: si los dos difieren, el preflight imprime un código inútil, se emite una
/// licencia contra ese código, y la activación falla EN EL MUNICIPIO con "la licencia fue
/// emitida para otra máquina" -- un mensaje que manda a buscar el problema al lugar
/// equivocado (¿reinstalaron el SO?) cuando en realidad el bug está en el script. Sin este
/// test, el defecto se descubre sin internet y sin poder volver al servidor.
///
/// El borde que importa es el Trim: FingerprintMaquinaLinux.ObtenerIdCrudo hace
/// File.ReadAllText(ruta).Trim() (FingerprintMaquinaLinux.cs:18) y systemd escribe
/// /etc/machine-id CON newline final. Un snippet bash que no trimee no da un valor "parecido":
/// da un hash COMPLETAMENTE distinto (verificado: 4617-8EFE-... con trim vs DBDA-82E1-... sin
/// trim para el mismo archivo).
/// </summary>
public class FingerprintParidadBashTests
{
    // Misma técnica que FingerprintMaquinaTests.FingerprintFijo (:11-16), redeclarada acá
    // porque esa es private en su archivo.
    private sealed class FingerprintFijo : FingerprintMaquinaBase
    {
        private readonly string _id;
        public FingerprintFijo(string id) => _id = id;
        protected override string ObtenerIdCrudo() => _id;
    }

    private static string RutaLibFingerprint([CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeEsteTest = Path.GetDirectoryName(archivoDeEsteTest)!;
        return Path.GetFullPath(Path.Combine(dirDeEsteTest, "..", "..", "..",
            "deploy", "kit", "lib", "fingerprint.sh"));
    }

    private static string FingerprintSegunBash(string contenidoDelArchivo)
    {
        var lib = RutaLibFingerprint();
        Assert.True(File.Exists(lib), $"No se encontró la librería bash en: {lib}");

        var tmp = Path.Combine(Path.GetTempPath(), $"machine-id-{Guid.NewGuid():N}");
        File.WriteAllText(tmp, contenidoDelArchivo);
        try
        {
            var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(
                $"set -euo pipefail; source '{lib}'; fingerprint_de_archivo '{tmp}'");

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr);
            return stdout;
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Bash_CoincideConCSharp_ParaUnMachineIdConNewlineFinal()
    {
        // systemd escribe /etc/machine-id con newline final: este es el caso REAL.
        const string id = "d9f1c0a24b6e4f7a8c3b5d1e9f20a731";

        var segunBash = FingerprintSegunBash(id + "\n");
        var segunCSharp = new FingerprintFijo(id).CodigoAgrupado;

        Assert.Equal(segunCSharp, segunBash);
    }

    [Fact]
    public void Bash_CoincideConCSharp_SinNewlineFinal()
    {
        const string id = "d9f1c0a24b6e4f7a8c3b5d1e9f20a731";

        var segunBash = FingerprintSegunBash(id);
        var segunCSharp = new FingerprintFijo(id).CodigoAgrupado;

        Assert.Equal(segunCSharp, segunBash);
    }

    [Fact]
    public void Bash_TieneElMismoFormatoDe16BloquesHexMayuscula()
    {
        var codigo = FingerprintSegunBash("otra-maquina-cualquiera\n");

        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){15}$", codigo);
    }

    [Fact]
    public void Bash_DifiereEntreMachineIdsDistintos()
    {
        var a = FingerprintSegunBash("maquina-1\n");
        var b = FingerprintSegunBash("maquina-2\n");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Bash_FallaConMensajeClaroSiElArchivoNoExiste()
    {
        var lib = RutaLibFingerprint();

        var (exitCode, _, stderr) = EjecutorBash.Ejecutar(
            $"set -euo pipefail; source '{lib}'; fingerprint_de_archivo '/no/existe/machine-id'");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("machine-id", stderr);
    }
}
