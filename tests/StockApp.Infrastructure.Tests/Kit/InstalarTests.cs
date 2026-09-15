using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardián de deploy/kit/02-instalar.sh (Task 3.1, plan 2026-09-12, líneas ~1614-1730).
///
/// 02-instalar.sh es un wrapper de servidor/install.sh (que NO SE MODIFICA). No hay forma
/// honesta de testear el wrapper de punta a punta en esta suite: servidor/install.sh y
/// offline/ recién los puebla armar-kit.sh en la Fase 4, y el script real toca /etc/stockapp
/// y requiere root. Lo único verificable acá es la GUARDIA PRINCIPAL: correr el script sin que
/// exista /etc/stockapp/.env tiene que abortar (exit ≠0) nombrando 01-bootstrap.sh, en vez de
/// fallar más abajo con un error críptico de install.sh sobre un .env ausente.
///
/// Se verifica en un contenedor Ubuntu descartable (misma versión fijada en deploy/kit/VERSION)
/// en vez de mockear el filesystem: así el contenedor arranca "limpio" y GARANTIZA que
/// /etc/stockapp/.env no existe, sin depender de qué haya en la máquina que corre los tests
/// (que bien podría tener un .env real de una instalación de desarrollo). Mismo patrón de
/// "verificación real, no simulada" que EjecutorBash usa para toda la librería bash del kit.
/// </summary>
public class InstalarTests
{
    private static string RutaKit([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy", "kit"));
    }

    private static string VersionUbuntuDelKit()
    {
        var dirKit = RutaKit();
        var version = File.ReadAllText(Path.Combine(dirKit, "VERSION"));
        return version.Trim();
    }

    [Fact]
    public void Instalar_SinEnvDeBootstrap_AbortaYNombraElPasoFaltante()
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();

        // Plantilla sin interpolar (el script bash usa ${PIPESTATUS[0]}, que colisionaría con
        // la sintaxis de interpolación de C# si fuera un $"""..."""): se reemplazan los dos
        // placeholders a mano.
        //
        // El grep NO busca solo "01-bootstrap.sh": ese texto lo nombran DOS guardias distintas
        // del script (la del .env faltante y, más abajo, la del contenedor stockapp-pg que no
        // está corriendo) -- y el contenedor ubuntu:24.04 "pelado" de este test no tiene el
        // binario 'docker', así que la segunda guardia SIEMPRE dispara ahí, enmascarando una
        // rotura de la primera con un mensaje que igual contiene "01-bootstrap.sh". Verificado
        // por mutación (ver Task 3.1 del plan): invertir solo la guardia del .env con ese grep
        // más laxo seguía dando VERDE. Por eso acá se exige el texto LITERAL y ÚNICO de la
        // guardia principal ("No existe /etc/stockapp/.env"), que ninguna otra guardia imprime.
        var plantilla = """
            docker run --rm -v "__DIR_KIT__:/kit:ro" "ubuntu:__UBUNTU__" bash -c '
              /kit/02-instalar.sh 2>&1 | tee /tmp/salida
              echo "EXIT=${PIPESTATUS[0]}"
              grep -q "No existe /etc/stockapp/.env" /tmp/salida && grep -q "01-bootstrap.sh" /tmp/salida && echo "NOMBRA_EL_PASO_FALTANTE=si"'
            """;
        var script = plantilla.Replace("__DIR_KIT__", dirKit).Replace("__UBUNTU__", ubuntu);

        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(script);

        // El exit code de 'docker run' refleja el ÚLTIMO comando del bash -c interno (los grep
        // && echo, que sale 0 si matchearon), no el exit del wrapper -- por eso el wrapper
        // imprime su propio "EXIT=..." y lo verificamos por texto, tal como especifica el plan.
        Assert.Equal(0, exitCode);
        Assert.Contains("EXIT=1", stdout);
        Assert.Contains("NOMBRA_EL_PASO_FALTANTE=si", stdout);
    }
}
