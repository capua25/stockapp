using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/kit/01-bootstrap.sh (Task 3.4, plan 2026-09-12, líneas ~2182-2461).
///
/// El plan declaraba este script "no se puede verificar acá" (instala paquetes del sistema,
/// arranca Docker, escribe en /etc, levanta contenedores) -- eso se escribió ANTES de que
/// existiera el patrón de stub-por-PATH que ya usan PreflightTests/LicenciaTests/VerificarTests.
/// Con ese patrón sí se pueden custodiar las REGLAS del script de forma determinística: se
/// stubean docker, dpkg, openssl, curl, systemctl, ip, ufw, pg_isready y pg_dump -- todos
/// ausentes o no controlables en un contenedor ubuntu:24.04 "pelado" (verificado: ese contenedor
/// no trae ip/openssl/curl/systemctl de fábrica) -- corriendo dentro de un contenedor
/// descartable (mismo patrón que InstalarTests/LicenciaTests) para que /etc/stockapp y
/// /opt/stockapp sean SIEMPRE del contenedor, nunca de la máquina que corre los tests.
///
/// deploy/kit/servidor/ todavía NO existe: lo puebla armar-kit.sh (Fase 4), que no corrió
/// todavía. Para los tests que necesitan llegar hasta el final del script (idempotencia,
/// firewall) se sintetiza un servidor/ MÍNIMO de prueba (compose dummy + wait-for-postgres.sh
/// que sale 0 sin depender de pg_isready real), copiado DENTRO del contenedor junto a una copia
/// de deploy/kit -- así DIR_KIT sigue resolviendo a un único directorio consistente y el script
/// real (sin modificar un solo carácter para el test) no necesita saber que está bajo test.
///
/// Lo que NO se prueba acá (queda para la Fase 5, VM real): la instalación real de paquetes vía
/// dpkg/apt, que Docker/systemd/ufw hagan lo que dicen que hacen, y que Postgres realmente quede
/// escuchando en 5433. Ver el reporte final de la Task 3.4 para el detalle de esas mutaciones
/// pendientes.
/// </summary>
public class BootstrapTests
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
            "deploy", "kit", "01-bootstrap.sh"));
    }

    private static string VersionUbuntuDelKit()
    {
        var version = File.ReadAllText(Path.Combine(RutaKit(), "VERSION"));
        return version.Trim();
    }

    // ---------- Stubs compartidos ----------
    //
    // Verificado en ubuntu:24.04 "pelado" (docker run --rm ubuntu:24.04 ...): NO trae ip,
    // openssl, curl ni systemctl. dpkg SÍ está (es la base de la imagen), pero se stubea igual
    // para no depender de su base de paquetes real (postgresql-client-16 nunca va a estar
    // "instalado" de verdad en el contenedor de test).

    private const string DockerStub = "#!/usr/bin/env bash\nexit 0\n";

    private const string DpkgStub = """
        #!/usr/bin/env bash
        case "$1" in
            -s) exit 0 ;;
            -i) echo "DPKG_I_LLAMADO $*" >> /tmp/rastro-dpkg-i; exit 0 ;;
            *) exit 0 ;;
        esac
        """;

    private const string OpensslStub = """
        #!/usr/bin/env bash
        if [[ "$1" == "rand" ]]; then
            echo "ZmFrZS1yYW5kb20tdmFsdWUtcGFyYS10ZXN0cw=="
            exit 0
        fi
        exit 0
        """;

    private const string CurlStub = "#!/usr/bin/env bash\nexit 0\n";
    private const string SystemctlStub = "#!/usr/bin/env bash\nexit 0\n";
    private const string PgIsreadyStub = "#!/usr/bin/env bash\nexit 0\n";
    private const string PgDumpStub = "#!/usr/bin/env bash\necho 'pg_dump (PostgreSQL) 16.4'\nexit 0\n";

    private const string IpStub = """
        #!/usr/bin/env bash
        echo "2: eth0    inet 192.168.1.50/24 brd 192.168.1.255 scope global eth0"
        exit 0
        """;

    /// <summary>Deja rastro en /tmp/rastro-ufw si alguna vez se invoca -- el guardián de la
    /// Decisión 12 afirma la AUSENCIA de ese archivo, no que 'ufw' no esté en el PATH.</summary>
    private const string UfwStub = """
        #!/usr/bin/env bash
        echo "UFW_LLAMADO $*" >> /tmp/rastro-ufw
        exit 0
        """;

    private const string ComposeDummy = "services:\n  stockapp-pg:\n    image: postgres:16-alpine\n";

    /// <summary>Standing-in por lo que armar-kit.sh (Fase 4) todavía no generó: el
    /// wait-for-postgres.sh real llama a pg_isready en loop, acá alcanza con salir 0.</summary>
    private const string WaitForPostgresDummy = "#!/usr/bin/env bash\nexit 0\n";

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static string CrearTestbin(bool incluirUfw = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bootstrap-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        EscribirEjecutable(Path.Combine(dir, "docker"), DockerStub);
        EscribirEjecutable(Path.Combine(dir, "dpkg"), DpkgStub);
        EscribirEjecutable(Path.Combine(dir, "openssl"), OpensslStub);
        EscribirEjecutable(Path.Combine(dir, "curl"), CurlStub);
        EscribirEjecutable(Path.Combine(dir, "systemctl"), SystemctlStub);
        EscribirEjecutable(Path.Combine(dir, "pg_isready"), PgIsreadyStub);
        EscribirEjecutable(Path.Combine(dir, "pg_dump"), PgDumpStub);
        EscribirEjecutable(Path.Combine(dir, "ip"), IpStub);
        if (incluirUfw)
            EscribirEjecutable(Path.Combine(dir, "ufw"), UfwStub);
        return dir;
    }

    private static string CrearFixtureServidor()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bootstrap-servidor-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.postgres.yml"), ComposeDummy);
        EscribirEjecutable(Path.Combine(dir, "wait-for-postgres.sh"), WaitForPostgresDummy);
        return dir;
    }

    /// <summary>
    /// Corre <paramref name="comando"/> (ya referido a /tmp/kit/01-bootstrap.sh) dentro de un
    /// contenedor ubuntu (root por defecto) descartable. El script corre desde una COPIA en
    /// /tmp/kit (no desde /kit directamente): así se le puede agregar el servidor/ que
    /// armar-kit.sh (Fase 4) todavía no generó, sin tocar el /kit:ro real.
    ///
    /// El entrypoint y el stdin (si hace falta) se escriben a ARCHIVOS DE VERDAD montados en el
    /// contenedor, no a un heredoc anidado dentro de un 'bash -c' -- mismo motivo que
    /// LicenciaTests: comando puede traer sus propias comillas simples (p.ej. --puerto '') y
    /// un 'printf ... | comando' embebido en un 'bash -c' con comillas simples se corta en la
    /// primera comilla del contenido, produciendo un script roto en silencio.
    ///
    /// Devuelve el exit code REAL del script (marcador EXIT=), no el de 'docker run'.
    /// </summary>
    private static (int ExitCode, string Salida) Correr(
        string comando,
        string? testbin = null,
        string? fixtureServidor = null,
        string? stdin = null)
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();

        var dirEntrypoint = Path.Combine(Path.GetTempPath(), "bootstrap-run-" + Guid.NewGuid());
        Directory.CreateDirectory(dirEntrypoint);
        try
        {
            var prepararKit = "mkdir -p /tmp/kit\ncp -r /kit/. /tmp/kit/\nchmod +x /tmp/kit/*.sh\n";
            if (fixtureServidor is not null)
            {
                prepararKit +=
                    "mkdir -p /tmp/kit/servidor\n" +
                    "cp /fixtures/docker-compose.postgres.yml /tmp/kit/servidor/\n" +
                    "cp /fixtures/wait-for-postgres.sh /tmp/kit/servidor/\n" +
                    "chmod +x /tmp/kit/servidor/wait-for-postgres.sh\n";
            }

            var pathExport = testbin is not null ? "export PATH=\"/testbin:$PATH\"\n" : "";
            var entrada = "";
            if (stdin is not null)
            {
                File.WriteAllText(Path.Combine(dirEntrypoint, "stdin.txt"), stdin);
                entrada = "cat /entrypoint/stdin.txt | ";
            }

            var entrypoint = $"""
                #!/usr/bin/env bash
                set -u
                {pathExport}{prepararKit}
                {entrada}{comando} > /tmp/salida-bootstrap 2>&1
                echo "EXIT=$?" >> /tmp/salida-bootstrap
                cat /tmp/salida-bootstrap
                echo "---RASTRO-DPKG-I---"
                cat /tmp/rastro-dpkg-i 2>/dev/null || echo AUSENTE
                echo "---RASTRO-UFW---"
                cat /tmp/rastro-ufw 2>/dev/null || echo AUSENTE
                echo "---ENV-EXISTE---"
                [[ -f /etc/stockapp/.env ]] && echo SI || echo NO
                echo "---ENV-CONTENIDO-B64---"
                base64 -w0 /etc/stockapp/.env 2>/dev/null || echo AUSENTE
                echo
                echo "---POST-INSTALACION---"
                cat /etc/stockapp/POST-INSTALACION.txt 2>/dev/null || echo AUSENTE

                """;
            File.WriteAllText(Path.Combine(dirEntrypoint, "run.sh"), entrypoint);

            var montajes = $"-v \"{dirKit}:/kit:ro\" -v \"{dirEntrypoint}:/entrypoint:ro\"";
            if (testbin is not null) montajes += $" -v \"{testbin}:/testbin:ro\"";
            if (fixtureServidor is not null) montajes += $" -v \"{fixtureServidor}:/fixtures:ro\"";

            var comandoDocker =
                $"docker run --rm {montajes} \"ubuntu:{ubuntu}\" bash /entrypoint/run.sh";

            var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(comandoDocker);
            Assert.True(exitCodeDocker == 0, $"'docker run' falló. stdout={stdout} stderr={stderr}");

            var match = Regex.Match(stdout, @"EXIT=(-?\d+)\s*$", RegexOptions.Multiline);
            Assert.True(match.Success, $"No se encontró el marcador EXIT= en la salida: {stdout}");
            return (int.Parse(match.Groups[1].Value), stdout);
        }
        finally
        {
            Directory.Delete(dirEntrypoint, recursive: true);
        }
    }

    /// <summary>Extrae el texto entre dos marcadores de "---xxx---" impresos por Correr().</summary>
    private static string Seccion(string salida, string encabezado, string? siguienteEncabezado = null)
    {
        var inicio = salida.IndexOf(encabezado, StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"No se encontró la sección '{encabezado}' en: {salida}");
        inicio += encabezado.Length;
        var fin = siguienteEncabezado is null
            ? salida.Length
            : salida.IndexOf(siguienteEncabezado, inicio, StringComparison.Ordinal);
        Assert.True(fin >= 0, $"No se encontró la sección '{siguienteEncabezado}' en: {salida}");
        return salida[inicio..fin].Trim('\n', '\r');
    }

    // ---------- Guardián de root ----------

    [Fact]
    public void SinRoot_AbortaConMensajeClaro()
    {
        var script = RutaScript();
        Assert.True(File.Exists(script), $"No se encontró el script en: {script}");

        // 01-bootstrap.sh no tiene subcomandos (a diferencia de 04-licencia.sh): el único
        // argumento válido es --puerto. Correrlo sin argumentos alcanza para llegar a la
        // guardia de EUID, que es lo que este test custodia.
        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar($"bash '{script}'");
        var salida = stdout + stderr;

        Assert.NotEqual(0, exitCode);
        Assert.Contains("necesita root", salida);
    }

    // ---------- Guardián de --puerto inválido ----------

    [Theory]
    [InlineData("99999")]
    [InlineData("abc")]
    [InlineData("")]
    public void PuertoInvalido_RechazadoConMensajeClaro_SinEscribirNada(string puertoInvalido)
    {
        var comando = string.IsNullOrEmpty(puertoInvalido)
            ? "/tmp/kit/01-bootstrap.sh --puerto ''"
            : $"/tmp/kit/01-bootstrap.sh --puerto {puertoInvalido}";

        var (exitCode, salida) = Correr(comando);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("no es un puerto válido", salida);
        Assert.Contains("---ENV-EXISTE---\nNO", salida);
    }

    // ---------- Docker ya presente y corriendo: no se reinstala ----------

    [Fact]
    public void Docker_YaInstaladoYCorriendo_NoInvocaDpkgInstalarYNoLoToca()
    {
        var testbin = CrearTestbin();
        try
        {
            // Solo nos importa la sección de Docker: alcanza con dejar que el script siga su
            // curso (puede fallar más adelante por falta de servidor/, no se provee ese fixture
            // acá a propósito) mientras capturamos el rastro de dpkg -i.
            var (_, salida) = Correr("/tmp/kit/01-bootstrap.sh", testbin: testbin);

            Assert.Contains("Docker ya está instalado y corriendo", salida);
            Assert.Contains("---RASTRO-DPKG-I---\nAUSENTE", salida);
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
        }
    }

    // ---------- LA GUARDIA MÁS IMPORTANTE: .env preexistente no se pisa ----------

    [Fact]
    public void EnvPreexistente_NoSePisa_ElContenidoQuedaByteAByteIntacto()
    {
        var testbin = CrearTestbin();
        try
        {
            const string contenidoSenuelo =
                "POSTGRES_PASSWORD=CONTRASENA-DE-PRODUCCION-QUE-YA-ESTA-EN-EL-VOLUMEN\n" +
                "JWT_SECRET=SECRETO-DE-PRODUCCION-QUE-NO-DEBE-CAMBIAR\n";
            var senueloB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(contenidoSenuelo));

            var comando =
                $"mkdir -p /etc/stockapp && printf '%s' '{contenidoSenuelo.Replace("'", "'\\''")}' " +
                "> /etc/stockapp/.env && chmod 600 /etc/stockapp/.env && " +
                "/tmp/kit/01-bootstrap.sh";

            var (_, salida) = Correr(comando, testbin: testbin);

            Assert.Contains("Ya existe /etc/stockapp/.env — NO se toca", salida);

            var seccionContenido = Seccion(salida, "---ENV-CONTENIDO-B64---", "---POST-INSTALACION---");
            Assert.Equal(senueloB64, seccionContenido.Trim());
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
        }
    }

    // ---------- Clave pública de licenciamiento vacía ----------

    [Fact]
    public void ClavePublicaVacia_ErrorFatalYNoEscribeElEnv()
    {
        var testbin = CrearTestbin();
        try
        {
            // 4 líneas de stdin: usuario (default), password válida x2, clave pública VACÍA.
            const string stdin = "\nContrasenaValida1\nContrasenaValida1\n\n";

            var (exitCode, salida) = Correr("/tmp/kit/01-bootstrap.sh", testbin: testbin, stdin: stdin);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("La clave pública es obligatoria", salida);
            Assert.Contains("---ENV-EXISTE---\nNO", salida);
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
        }
    }

    // ---------- Idempotencia: correr dos veces respeta el .env de la primera ----------

    [Fact]
    public void Idempotencia_CorridaDoble_RespetaElEnvDeLaPrimeraYNoRompeNada()
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();
        var testbin = CrearTestbin();
        var fixtureServidor = CrearFixtureServidor();
        try
        {
            var entrypoint = """
                #!/usr/bin/env bash
                set -u
                export PATH="/testbin:$PATH"

                mkdir -p /tmp/kit
                cp -r /kit/. /tmp/kit/
                mkdir -p /tmp/kit/servidor
                cp /fixtures/docker-compose.postgres.yml /tmp/kit/servidor/
                cp /fixtures/wait-for-postgres.sh /tmp/kit/servidor/
                chmod +x /tmp/kit/*.sh /tmp/kit/servidor/wait-for-postgres.sh

                printf '\nContrasenaValida1\nContrasenaValida1\nCLAVE-PUBLICA-DE-PRUEBA-UNO==\n' \
                    | /tmp/kit/01-bootstrap.sh
                echo "EXIT1=$?"
                cp /etc/stockapp/.env /tmp/env-corrida-1

                printf '\nOtraContrasenaXX1\nOtraContrasenaXX1\nCLAVE-PUBLICA-DISTINTA-DOS==\n' \
                    | /tmp/kit/01-bootstrap.sh > /tmp/salida-corrida-2 2>&1
                echo "EXIT2=$?"
                cat /tmp/salida-corrida-2
                cp /etc/stockapp/.env /tmp/env-corrida-2

                if cmp -s /tmp/env-corrida-1 /tmp/env-corrida-2; then
                    echo "ENV_IDENTICO=si"
                else
                    echo "ENV_IDENTICO=no"
                fi
                """;

            var dirEntrypoint = Path.Combine(Path.GetTempPath(), "bootstrap-entrypoint-" + Guid.NewGuid());
            Directory.CreateDirectory(dirEntrypoint);
            var rutaEntrypoint = Path.Combine(dirEntrypoint, "entrypoint.sh");
            File.WriteAllText(rutaEntrypoint, entrypoint);
            try
            {
                var comando =
                    $"docker run --rm -v \"{dirKit}:/kit:ro\" -v \"{testbin}:/testbin:ro\" " +
                    $"-v \"{fixtureServidor}:/fixtures:ro\" -v \"{dirEntrypoint}:/entrypoint:ro\" " +
                    $"\"ubuntu:{ubuntu}\" bash /entrypoint/entrypoint.sh";

                var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(comando);
                var salida = stdout + stderr;
                Assert.True(exitCodeDocker == 0, $"'docker run' falló. salida={salida}");

                Assert.Contains("EXIT1=0", salida);
                Assert.Contains("EXIT2=0", salida);
                Assert.Contains("Ya existe /etc/stockapp/.env — NO se toca", salida);
                Assert.Contains("ENV_IDENTICO=si", salida);
            }
            finally
            {
                Directory.Delete(dirEntrypoint, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
            Directory.Delete(fixtureServidor, recursive: true);
        }
    }

    // ---------- Decisión 12: el firewall nunca se ejecuta, solo se imprime y se persiste ----------

    [Fact]
    public void Firewall_NuncaInvocaUfw_ImprimeYPersisteComandosConValoresExpandidos()
    {
        var testbin = CrearTestbin(incluirUfw: true);
        var fixtureServidor = CrearFixtureServidor();
        try
        {
            const string stdin = "\nContrasenaValida1\nContrasenaValida1\nCLAVE-PUBLICA-FIREWALL==\n";

            var (exitCode, salida) = Correr(
                "/tmp/kit/01-bootstrap.sh --puerto 6123",
                testbin: testbin,
                fixtureServidor: fixtureServidor,
                stdin: stdin);

            Assert.Equal(0, exitCode);

            // Nunca se invoca ufw: el stub deja rastro si se lo llama, y el rastro no existe.
            Assert.Contains("---RASTRO-UFW---\nAUSENTE", salida);

            // El aviso por stdout usa el puerto REAL (6123), no un placeholder.
            Assert.Contains("Este kit NO activa el firewall por vos", salida);
            Assert.Contains("sudo ufw allow from 192.168.1.50/24 to any port 6123 proto tcp", salida);

            // POST-INSTALACION.txt persiste (no es "AUSENTE"), con los mismos valores expandidos.
            var postInstalacion = Seccion(salida, "---POST-INSTALACION---");
            Assert.DoesNotContain("AUSENTE", postInstalacion[..Math.Min(20, postInstalacion.Length)]);
            Assert.Contains(
                "sudo ufw allow from 192.168.1.50/24 to any port 6123 proto tcp", postInstalacion);
            Assert.Contains("FIREWALL: este kit NO activa ufw automáticamente", postInstalacion);
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
            Directory.Delete(fixtureServidor, recursive: true);
        }
    }
}
