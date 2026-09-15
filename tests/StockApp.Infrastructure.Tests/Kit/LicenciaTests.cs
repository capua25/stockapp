using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/kit/04-licencia.sh (Task 3.2, plan 2026-09-12, líneas ~1733-1930).
///
/// El script necesita root porque lee /etc/stockapp/.env (600, dueño root) para resolver
/// API_PORT vía resolver_api_port (lib/validaciones.sh) -- sin root esa lectura falla en
/// silencio y el script cae al puerto por defecto en vez de avisar del problema real. Por eso
/// se agregó una guardia explícita de EUID==0, igual que 02-instalar.sh y 03-verificar.sh.
///
/// Las guardias de "activar" (archivo faltante/inexistente/vacío) y de subcomando desconocido
/// se verifican DESPUÉS de la guardia de root, así que hay que correrlas dentro de un contenedor
/// Ubuntu descartable (mismo patrón que InstalarTests.cs): el usuario por defecto de la imagen
/// es root, así que la guardia de EUID pasa sola y se puede llegar a las guardias de más abajo.
/// La guardia de root en sí se prueba SIN docker, corriendo el script tal cual con el usuario
/// no-root que corre esta suite -- eso es justamente lo que hay que garantizar que dispare.
///
/// No se necesita la API arriba para ninguna de estas: todas las guardias que se cubren acá
/// pasan ANTES de que el script intente un curl.
/// </summary>
public class LicenciaTests
{
    private static string RutaKit([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy", "kit"));
    }

    private static string RutaScript([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..",
            "deploy", "kit", "04-licencia.sh"));
    }

    private static string VersionUbuntuDelKit()
    {
        var version = File.ReadAllText(Path.Combine(RutaKit(), "VERSION"));
        return version.Trim();
    }

    /// <summary>
    /// Corre "/kit/04-licencia.sh &lt;argsDentroDelKit&gt;" dentro de un contenedor ubuntu
    /// descartable (usuario root por defecto), con /kit montado read-only sobre deploy/kit.
    /// Devuelve el exit code REAL del script (leído del marcador EXIT=, no el de 'docker run',
    /// que refleja el último comando del bash -c interno) y el stdout+stderr combinados.
    /// </summary>
    private static (int ExitCode, string Salida) EjecutarComoRootEnContainer(string comandoDentroDelKit)
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();

        var plantilla = """
            docker run --rm -v "__DIR_KIT__:/kit:ro" "ubuntu:__UBUNTU__" bash -c '
              __COMANDO__ 2>&1 | tee /tmp/salida
              echo "EXIT=${PIPESTATUS[0]}"'
            """;
        var script = plantilla
            .Replace("__DIR_KIT__", dirKit)
            .Replace("__UBUNTU__", ubuntu)
            .Replace("__COMANDO__", comandoDentroDelKit);

        var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(script);
        Assert.True(exitCodeDocker == 0, $"'docker run' falló. stdout={stdout} stderr={stderr}");

        var match = Regex.Match(stdout, @"EXIT=(\d+)\s*$");
        Assert.True(match.Success, $"No se encontró el marcador EXIT= en la salida: {stdout}");
        return (int.Parse(match.Groups[1].Value), stdout);
    }

    // ---------- Guardián de root ----------

    [Fact]
    public void SinRoot_AbortaConMensajeClaro()
    {
        var script = RutaScript();
        Assert.True(File.Exists(script), $"No se encontró el script en: {script}");

        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar($"bash '{script}' fingerprint");
        var salida = stdout + stderr;

        Assert.NotEqual(0, exitCode);
        Assert.Contains("necesita root", salida);
    }

    // ---------- Guardián de "activar": archivo faltante ----------

    [Fact]
    public void Activar_SinArgumentoDeArchivo_ErrorClaro()
    {
        var (exitCode, salida) = EjecutarComoRootEnContainer("/kit/04-licencia.sh activar");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Falta el archivo de licencia", salida);
    }

    // ---------- Guardián de "activar": archivo inexistente ----------

    [Fact]
    public void Activar_ArchivoInexistente_ErrorClaro()
    {
        var (exitCode, salida) = EjecutarComoRootEnContainer(
            "/kit/04-licencia.sh activar /no/existe/licencia.txt");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("No existe el archivo de licencia", salida);
    }

    // ---------- Guardián de "activar": archivo vacío ----------

    [Fact]
    public void Activar_ArchivoVacio_ErrorClaro()
    {
        var (exitCode, salida) = EjecutarComoRootEnContainer(
            "touch /tmp/licencia-vacia.txt && /kit/04-licencia.sh activar /tmp/licencia-vacia.txt");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("está vacío", salida);
    }

    // ---------- Guardián de subcomando desconocido / ausente ----------

    [Fact]
    public void SubcomandoDesconocido_MuestraUsoYSaleConError()
    {
        var (exitCode, salida) = EjecutarComoRootEnContainer("/kit/04-licencia.sh algomal");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Uso:", salida);
    }

    [Fact]
    public void SinSubcomando_MuestraUsoYSaleConError()
    {
        var (exitCode, salida) = EjecutarComoRootEnContainer("/kit/04-licencia.sh");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Uso:", salida);
    }

    // ---------- Guardián de seguridad: symlink attack sobre la respuesta de "activar" ----------

    /// <summary>
    /// deploy/kit/04-licencia.sh corre como root en el servidor del municipio. Si "activar"
    /// escribe la respuesta del POST en una ruta FIJA y predecible bajo /tmp (world-writable),
    /// cualquier usuario sin privilegios puede crear esa ruta de antemano como symlink a
    /// /etc/shadow, /etc/passwd o /etc/stockapp/.env. 'curl -o' sigue symlinks, y como el script
    /// corre con privilegios de root, sobrescribiría el destino con la respuesta del servidor.
    /// El sticky bit de /tmp impide BORRAR archivos ajenos; no impide CREARLOS antes que el
    /// script arranque.
    ///
    /// El guardián real: pre-crear /tmp/licencia-respuesta.json como symlink a un señuelo DENTRO
    /// del contenedor descartable, correr "activar", y afirmar que el señuelo NO cambió. Se
    /// stubea 'curl' (no toca la red, así que no hace falta la API real arriba) escribiendo el
    /// cuerpo de la respuesta con '>' -- la misma redirección de shell sigue symlinks exactamente
    /// igual que 'curl -o', así que alcanza para reproducir la semántica que hace explotable una
    /// ruta fija, sin necesitar curl real ni red.
    ///
    /// El script y el stub de curl se escriben a archivos de verdad (no a un heredoc anidado
    /// dentro del 'bash -c' del contenedor) para no toparse con quoting de tres niveles: los
    /// scripts de más abajo usan comillas simples sin restricción.
    /// </summary>
    [Fact]
    public void Activar_NoSigueUnSymlinkPreexistenteEnRutaFijaDeTmp_ElSenueloQuedaIntacto()
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();

        var dirTestbin = Path.Combine(Path.GetTempPath(), "licencia-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dirTestbin);
        try
        {
            var rutaCurlFalso = Path.Combine(dirTestbin, "curl");
            File.WriteAllText(rutaCurlFalso, CurlFalsoScript);
            File.SetUnixFileMode(rutaCurlFalso,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var rutaEntrypoint = Path.Combine(dirTestbin, "entrypoint.sh");
            File.WriteAllText(rutaEntrypoint, EntrypointScript);

            var comando =
                $"docker run --rm -v \"{dirKit}:/kit:ro\" -v \"{dirTestbin}:/testbin:ro\" " +
                $"\"ubuntu:{ubuntu}\" bash /testbin/entrypoint.sh";

            var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(comando);
            var salida = stdout + stderr;
            Assert.True(exitCodeDocker == 0, $"'docker run' falló. salida={salida}");

            Assert.Contains("SENUELO_INTACTO=si", salida);
        }
        finally
        {
            Directory.Delete(dirTestbin, recursive: true);
        }
    }

    private const string CurlFalsoScript = """
        #!/usr/bin/env bash
        # Fake curl para tests: no toca la red. Simula el GET a /licencia/estado (sin -X) y el
        # POST a /licencia/activar (-X POST ... -o <archivo>), escribiendo el cuerpo con '>' --
        # la misma redirección de shell sigue symlinks igual que 'curl -o' real.
        es_post=0
        outfile=""
        args=("$@")
        i=0
        while [[ $i -lt ${#args[@]} ]]; do
            case "${args[$i]}" in
                -X) i=$((i+1)); [[ "${args[$i]}" == "POST" ]] && es_post=1 ;;
                -o) i=$((i+1)); outfile="${args[$i]}" ;;
            esac
            i=$((i+1))
        done

        if [[ "$es_post" -eq 1 ]]; then
            printf '%s' 'CUERPO_DE_PRUEBA_SIN_MATCH' > "$outfile"
            printf '%s' '500'
        else
            printf '%s' '{"activada":false,"codigoMaquina":"TEST-0000"}'
        fi
        exit 0
        """;

    private const string EntrypointScript = """
        #!/usr/bin/env bash
        set -u
        export PATH="/testbin:$PATH"

        echo 'CONTENIDO_ORIGINAL' > /root/senuelo.txt
        rm -f /tmp/licencia-respuesta.json
        ln -s /root/senuelo.txt /tmp/licencia-respuesta.json

        echo 'LICENCIA-DE-PRUEBA' > /tmp/lic.txt

        /kit/04-licencia.sh activar /tmp/lic.txt
        echo "EXIT_ACTIVAR=$?"

        SENUELO="$(cat /root/senuelo.txt)"
        if [[ "$SENUELO" == "CONTENIDO_ORIGINAL" ]]; then
            echo "SENUELO_INTACTO=si"
        else
            echo "SENUELO_INTACTO=no (quedo: ${SENUELO})"
        fi
        """;
}
