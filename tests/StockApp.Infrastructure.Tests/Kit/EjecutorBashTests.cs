using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardián de tests/_Compartido/EjecutorBash.cs -- encontrado investigando el rojo intermitente
/// de <see cref="BootstrapTests.PuertoInvalido_RechazadoConMensajeClaro_SinEscribirNada"/> del
/// deploy no-op real (2026-09-16, 590/591).
///
/// Diagnóstico: en aislamiento (5 corridas) y en la suite completa de Kit (3 corridas) el rojo
/// NUNCA se reprodujo -- ni siquiera con el mismo caso parametrizado ("99999") que falló esa
/// vez. Se reprodujo recién forzando contención real: 4 procesos "dotnet test" de la suite Kit
/// corriendo EN PARALELO entre sí. Ahí fallaron DOS tests DISTINTOS en corridas DISTINTAS --
/// "PuertoInvalido...(abc)" en una, "DeployVpsTests.BackupDeCeroBytes_Aborta" (que ni siquiera
/// usa Docker) en otra -- lo que descarta un bug determinístico ligado al valor "99999" o a
/// 01-bootstrap.sh (sin tocar por los commits de hoy, confirmado con git log) y apunta a una
/// causa compartida: EjecutorBash.Ejecutar().
///
/// La causa real: EjecutorBash.Ejecutar() leía StandardOutput.ReadToEnd() COMPLETO antes de
/// leer StandardError -- el anti-patrón clásico de System.Diagnostics.Process documentado por
/// Microsoft ("Do not read the standard output and standard error streams sequentially"). Bajo
/// carga liviana nunca se manifiesta (las pipes de SO tienen ~64KB de buffer, de sobra para la
/// salida chica de estos scripts). Bajo contención real (muchos procesos "docker run"/bash
/// compitiendo, el caso del deploy no-op) alcanza con que la salida (stdout+stderr combinados
/// por el propio script, o ruido del cliente docker) se acerque a ese límite para que la lectura
/// se trunque o el proceso se cuelgue -- exactamente el síntoma que se vio: "No se encontró el
/// marcador EXIT= en la salida" con el proceso ya reportando exit code 0.
///
/// Este guardián NO depende de contención real (no es reproducible de forma confiable): fuerza
/// el mismo mecanismo escribiendo más de 64KB intercalados en AMBOS streams desde el script bash
/// -- eso alcanza para colgar la implementación vieja (secuencial) de forma determinística, sin
/// necesidad de otros procesos compitiendo. Con timeout explícito para que un guardián roto
/// falle rápido en vez de colgar la corrida entera.
/// </summary>
public class EjecutorBashTests
{
    [Fact(Timeout = 15000)]
    public void CapturaStdoutYStderrCompletos_AunqueSuperenElBufferDeLaPipeDelSistemaOperativo()
    {
        // >64KB intercalados en cada stream (2000 líneas de 81 bytes ~ 162KB por stream) --
        // supera de sobra el tamaño típico de pipe buffer de Linux (64KB) que dispara el
        // anti-patrón de lectura secuencial si el proceso hijo se queda esperando a que alguien
        // vacíe el otro stream.
        const string script = """
            for i in $(seq 1 2000); do
                printf 'S%079d\n' "$i"
                printf 'E%079d\n' "$i" >&2
            done
            echo FIN-STDOUT
            echo FIN-STDERR >&2
            """;

        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(script);

        Assert.Equal(0, exitCode);

        var lineasStdout = stdout.Split('\n');
        var lineasStderr = stderr.Split('\n');

        Assert.Equal(2001, lineasStdout.Length);
        Assert.Equal(2001, lineasStderr.Length);
        Assert.Equal("FIN-STDOUT", lineasStdout[^1]);
        Assert.Equal("FIN-STDERR", lineasStderr[^1]);
        // Ninguna línea intermedia se perdió ni se cortó a la mitad -- confirma que la captura
        // llegó completa en ambos streams, no solo que el proceso reportó exit 0.
        Assert.Equal("S" + 2000.ToString("D79"), lineasStdout[^2]);
        Assert.Equal("E" + 2000.ToString("D79"), lineasStderr[^2]);
    }
}
