using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/install.sh (NO deploy/kit/02-instalar.sh, que es un wrapper distinto
/// probado en InstalarTests.cs) para dos cambios del 2026-09-17: API_SCHEME (http/https,
/// configurable) y el nuevo default de puerto 8080 (antes 5080).
///
/// Corre EN UN CONTENEDOR UBUNTU DESCARTABLE, COMO ROOT (mismo patrón que VerificarTests/
/// LicenciaTests/BootstrapTests): install.sh exige EUID=0 desde su primera línea, crea el
/// usuario de sistema 'stockapp' de verdad y (para API_SCHEME=https) necesita que el chequeo de
/// legibilidad de los certificados sea un chequeo de permisos UNIX REAL, no un stub que
/// simplemente devuelva 0 -- eso sería "teatro" (convención del proyecto: un stub que miente
/// hace que todos sus tests pasen contra una realidad inventada). Por eso 'sudo' se stubea con
/// un wrapper que delega en 'setpriv' (sí presente en ubuntu:24.04 "pelado", verificado con
/// 'docker run --rm ubuntu:24.04') para hacer un DROP DE PRIVILEGIOS DE VERDAD: un archivo con
/// permisos 000 realmente no es legible por 'stockapp' dentro del contenedor, y uno con 644
/// realmente sí lo es -- verificado empíricamente antes de escribir este archivo.
///
/// dpkg/curl/pg_isready/pg_dump/systemctl se stubean para que install.sh nunca intente apt-get
/// real (sin red determinística en CI) ni dependa de systemd real (no hay PID 1 de systemd en
/// un contenedor plano) -- mismo criterio que BootstrapTests. El curl stub responde 200 al
/// primer intento del healthcheck para no pagar el loop de 90x2s=180s de reintentos.
///
/// División deliberada de los chequeos de API_SCHEME=https en DOS bloques del script (ver
/// install.sh): variables-presentes + archivos-existen ANTES de tocar el sistema (no
/// necesitan que 'stockapp' exista); legibilidad-por-'stockapp' DESPUÉS de crear ese usuario
/// (es el único de los tres que genuinamente lo necesita). Fallar lo más rápido posible es
/// mejor UX y, de paso, simplifica los tests: la mayoría de los guardianes de acá abortan ANTES
/// de necesitar ningún stub de apt/systemd.
/// </summary>
public class InstallEsquemaTests
{
    private static string RutaDeploy([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy"));
    }

    private static string VersionUbuntuDelKit()
    {
        var version = File.ReadAllText(Path.Combine(RutaDeploy(), "kit", "VERSION"));
        return version.Trim();
    }

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Tarball MÍNIMO pero VÁLIDO (gzip real, con un ejecutable 'StockApp.Api' en la raíz):
    /// solo hace falta para los tests que llegan hasta "Extrayendo release" (los que verifican
    /// el contenido final de la unit). Los que abortan antes (validación temprana de
    /// API_SCHEME) ni siquiera necesitan que sea un gzip real -- 'tar -tzf' corre mucho después
    /// -- pero usar siempre uno válido evita tener que duplicar la lógica de armado.
    /// </summary>
    private static string CrearTarballValido()
    {
        var dirOrigen = Path.Combine(Path.GetTempPath(), "install-tarball-src-" + Guid.NewGuid());
        Directory.CreateDirectory(dirOrigen);
        var binario = Path.Combine(dirOrigen, "StockApp.Api");
        EscribirEjecutable(binario, "#!/usr/bin/env bash\nexit 0\n");

        var tarball = Path.Combine(Path.GetTempPath(), "install-tarball-" + Guid.NewGuid() + ".tar.gz");
        var (exit, _, stderr) = EjecutorBash.Ejecutar($"tar -czf '{tarball}' -C '{dirOrigen}' StockApp.Api");
        Directory.Delete(dirOrigen, recursive: true);
        Assert.True(exit == 0, $"No se pudo armar el tarball de prueba: {stderr}");
        return tarball;
    }

    private static string CrearTarballDummy()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "install-tarball-dummy-" + Guid.NewGuid() + ".tar.gz");
        File.WriteAllText(ruta, "no-es-un-gzip-de-verdad");
        return ruta;
    }

    // ---------- Stubs ----------

    private const string DpkgStub = "#!/usr/bin/env bash\nexit 0\n";
    private const string PgStub = "#!/usr/bin/env bash\nexit 0\n";

    /// <summary>Responde 200 al primer intento -- evita el loop de reintentos de 90x2s. Deja
    /// rastro de CADA invocación (con %q) en /tmp/curl-rastro para poder afirmar si el script
    /// usó '--resolve' (solo debería aparecer con API_SCHEME=https).</summary>
    private const string CurlStubRapido = """
        #!/usr/bin/env bash
        { printf 'ARGS:'; printf ' %q' "$@"; printf '\n'; } >> /tmp/curl-rastro
        exit 0
        """;

    private const string SystemctlStub = """
        #!/usr/bin/env bash
        case "$1" in
            is-active) exit 3 ;;
            *) exit 0 ;;
        esac
        """;

    /// <summary>
    /// 'sudo' no está en ubuntu:24.04 "pelado" (verificado). Este stub soporta el ÚNICO uso que
    /// hace install.sh ('sudo -u &lt;user&gt; &lt;cmd...&gt;') delegando en 'setpriv --reuid
    /// --regid --clear-groups', que SÍ está en la imagen base y hace un drop de privilegios
    /// real -- no es un stub que miente: un archivo con permisos 000 realmente falla acá.
    /// </summary>
    private const string SudoStub = """
        #!/usr/bin/env bash
        if [[ "$1" == "-u" ]]; then
            usuario="$2"; shift 2
            exec setpriv --reuid "$usuario" --regid "$usuario" --clear-groups "$@"
        fi
        exec "$@"
        """;

    private static string CrearTestbin(bool conSudo = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "install-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        EscribirEjecutable(Path.Combine(dir, "dpkg"), DpkgStub);
        EscribirEjecutable(Path.Combine(dir, "curl"), CurlStubRapido);
        EscribirEjecutable(Path.Combine(dir, "systemctl"), SystemctlStub);
        EscribirEjecutable(Path.Combine(dir, "pg_isready"), PgStub);
        EscribirEjecutable(Path.Combine(dir, "pg_dump"), PgStub);
        if (conSudo)
            EscribirEjecutable(Path.Combine(dir, "sudo"), SudoStub);
        return dir;
    }

    private const string EnvBase = """
        POSTGRES_USER=stockapp
        POSTGRES_PASSWORD=secreta-de-prueba-123
        POSTGRES_DB=stockapp
        JWT_SECRET=0123456789ABCDEF0123456789ABCDEF
        BOOTSTRAP_ADMIN_USER=admin
        BOOTSTRAP_PASSWORD=Admin12345
        LICENCIA_CLAVE_PUBLICA_BASE64=ZmFrZS1jbGF2ZS1wdWJsaWNh
        API_BIND=127.0.0.1
        """;

    /// <summary>
    /// Corre deploy/install.sh de verdad, como root, dentro de un contenedor descartable.
    /// Monta deploy/ (real, ro), el tarball y el .env pasados, y -- si se piden certificados
    /// TLS -- un directorio /certs con fullchain.pem/privkey.pem con los permisos indicados
    /// (644 = legible por todos, 000 = solo root). Devuelve el exit code REAL del script
    /// (marcador EXIT=), su salida combinada, el contenido de la unit generada (o "" si nunca
    /// se llegó a escribir) y el rastro de invocaciones de curl.
    /// </summary>
    private static (int ExitCode, string Salida, string Unit, string RastroCurl, string ApiEnv) EjecutarInstall(
        string envContent, string tarball, bool conTestbin, bool conSudo = false,
        int? permisosFullchain = null, int? permisosPrivkey = null)
    {
        var dirDeploy = RutaDeploy();
        var ubuntu = VersionUbuntuDelKit();

        var dirEntrypoint = Path.Combine(Path.GetTempPath(), "install-run-" + Guid.NewGuid());
        Directory.CreateDirectory(dirEntrypoint);
        var dirEnv = Path.Combine(Path.GetTempPath(), "install-env-" + Guid.NewGuid());
        Directory.CreateDirectory(dirEnv);
        var dirCerts = Path.Combine(Path.GetTempPath(), "install-certs-" + Guid.NewGuid());
        Directory.CreateDirectory(dirCerts);
        var dirTestbin = conTestbin ? CrearTestbin(conSudo) : null;
        try
        {
            File.WriteAllText(Path.Combine(dirEnv, ".env"), envContent);

            if (permisosFullchain is not null)
            {
                var f = Path.Combine(dirCerts, "fullchain.pem");
                File.WriteAllText(f, "----CERT-DE-PRUEBA----");
                File.SetUnixFileMode(f, (UnixFileMode)permisosFullchain.Value);
            }
            if (permisosPrivkey is not null)
            {
                var f = Path.Combine(dirCerts, "privkey.pem");
                File.WriteAllText(f, "----KEY-DE-PRUEBA----");
                File.SetUnixFileMode(f, (UnixFileMode)permisosPrivkey.Value);
            }

            var pathExport = conTestbin ? "export PATH=\"/testbin:$PATH\"\n" : "";
            var entrypoint = $"""
                #!/usr/bin/env bash
                set -u
                {pathExport}bash /deploy/install.sh /tarball.tar.gz /env/.env > /tmp/salida 2>&1
                echo "EXIT=$?" >> /tmp/salida
                cat /tmp/salida
                echo "---UNIT---"
                cat /etc/systemd/system/stockapp-api.service 2>/dev/null || echo AUSENTE
                echo "---CURL-RASTRO---"
                cat /tmp/curl-rastro 2>/dev/null || echo AUSENTE
                echo "---API-ENV---"
                cat /etc/stockapp-api/api.env 2>/dev/null || echo AUSENTE

                """;
            File.WriteAllText(Path.Combine(dirEntrypoint, "run.sh"), entrypoint);

            var montajes = $"-v \"{dirDeploy}:/deploy:ro\" -v \"{dirEntrypoint}:/entrypoint:ro\" " +
                $"-v \"{tarball}:/tarball.tar.gz:ro\" -v \"{dirEnv}:/env:ro\" -v \"{dirCerts}:/certs:ro\"";
            if (dirTestbin is not null) montajes += $" -v \"{dirTestbin}:/testbin:ro\"";

            var comandoDocker = $"docker run --rm {montajes} \"ubuntu:{ubuntu}\" bash /entrypoint/run.sh";

            var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(comandoDocker);
            Assert.True(exitCodeDocker == 0, $"'docker run' falló. stdout={stdout} stderr={stderr}");

            var match = System.Text.RegularExpressions.Regex.Match(stdout, @"EXIT=(-?\d+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
            Assert.True(match.Success, $"No se encontró el marcador EXIT= en la salida: {stdout}");
            var exitCode = int.Parse(match.Groups[1].Value);

            var unit = Seccion(stdout, "---UNIT---", "---CURL-RASTRO---");
            var rastroCurl = Seccion(stdout, "---CURL-RASTRO---", "---API-ENV---");
            var apiEnv = Seccion(stdout, "---API-ENV---", null);

            return (exitCode, stdout, unit, rastroCurl, apiEnv);
        }
        finally
        {
            Directory.Delete(dirEntrypoint, recursive: true);
            Directory.Delete(dirEnv, recursive: true);
            Directory.Delete(dirCerts, recursive: true);
            if (dirTestbin is not null) Directory.Delete(dirTestbin, recursive: true);
        }
    }

    private static string Seccion(string salida, string encabezado, string? siguienteEncabezado)
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

    // ---------- Default de puerto y esquema (sin variables -> retrocompatible) ----------

    [Fact]
    public void SinApiPortNiApiScheme_UsaLosDefaults8080YHttp()
    {
        var (_, salida, _, _, _) = EjecutarInstall(EnvBase, CrearTarballDummy(), conTestbin: false);

        Assert.Contains("OK. API_PORT=8080", salida);
        Assert.Contains("OK. API_SCHEME=http", salida);
    }

    // ---------- API_SCHEME inválido ----------

    [Theory]
    [InlineData("ftp")]
    [InlineData("HTTPS")]
    [InlineData("htpps")]
    public void ApiSchemeInvalido_AbortaConExitDistintoDeCero(string valorInvalido)
    {
        var env = EnvBase + $"\nAPI_SCHEME={valorInvalido}\n";
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballDummy(), conTestbin: false);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("API_SCHEME", salida);
        Assert.Contains("no es válido", salida);
    }

    /// <summary>
    /// Caso "vacío" aparte del Theory de arriba: a diferencia de API_PORT/API_BIND (que tratan
    /// "no definida" y "definida pero vacía" exactamente igual, con `${VAR:-default}`),
    /// API_SCHEME los distingue a propósito -- "no definida" (.env viejo) cae a http en
    /// silencio, pero "definida y vacía" (típicamente un typo tipo 'API_SCHEME=' sin valor) es
    /// un error real que install.sh tiene que frenar, no absorber como si fuera lo mismo que
    /// "no la pusiste". Ver el comentario en install.sh junto al chequeo.
    /// </summary>
    [Fact]
    public void ApiSchemeVacio_AbortaConExitDistintoDeCero()
    {
        var env = EnvBase + "\nAPI_SCHEME=\n";
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballDummy(), conTestbin: false);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("API_SCHEME", salida);
        Assert.Contains("no es válido", salida);
    }

    // ---------- API_SCHEME=https: variables de certificado obligatorias ----------

    [Fact]
    public void ApiSchemeHttps_SinKestrelPath_AbortaConExitDistintoDeCero()
    {
        var env = EnvBase + """

            API_SCHEME=https
            API_HOSTNAME=stockapp.test.local
            Kestrel__Certificates__Default__KeyPath=/certs/privkey.pem
            """;
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballDummy(), conTestbin: false,
            permisosPrivkey: 0b110_100_100);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("falta 'Kestrel__Certificates__Default__Path'", salida);
    }

    [Fact]
    public void ApiSchemeHttps_SinKestrelKeyPath_AbortaConExitDistintoDeCero()
    {
        var env = EnvBase + """

            API_SCHEME=https
            API_HOSTNAME=stockapp.test.local
            Kestrel__Certificates__Default__Path=/certs/fullchain.pem
            """;
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballDummy(), conTestbin: false,
            permisosFullchain: 0b110_100_100);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("falta 'Kestrel__Certificates__Default__KeyPath'", salida);
    }

    [Fact]
    public void ApiSchemeHttps_SinApiHostname_AbortaConExitDistintoDeCero()
    {
        var env = EnvBase + """

            API_SCHEME=https
            Kestrel__Certificates__Default__Path=/certs/fullchain.pem
            Kestrel__Certificates__Default__KeyPath=/certs/privkey.pem
            """;
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballDummy(), conTestbin: false,
            permisosFullchain: 0b110_100_100, permisosPrivkey: 0b110_100_100);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("falta 'API_HOSTNAME'", salida);
    }

    [Fact]
    public void ApiSchemeHttps_CertInexistente_AbortaConExitDistintoDeCero()
    {
        // Ninguno de los dos archivos se crea en /certs (permisosFullchain/permisosPrivkey en
        // null): las variables apuntan a rutas que genuinamente no existen.
        var env = EnvBase + """

            API_SCHEME=https
            API_HOSTNAME=stockapp.test.local
            Kestrel__Certificates__Default__Path=/certs/fullchain.pem
            Kestrel__Certificates__Default__KeyPath=/certs/privkey.pem
            """;
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballDummy(), conTestbin: false);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("no existe", salida);
        Assert.Contains("fullchain.pem", salida);
    }

    // ---------- API_SCHEME=https: legibilidad por 'stockapp' (necesita que el usuario exista) ----------

    /// <summary>
    /// Decisión tomada durante la implementación (simplificación pedida explícitamente): a
    /// diferencia de la EXISTENCIA de los certificados (guardia dura, aborta), la LEGIBILIDAD
    /// por 'stockapp' es un chequeo más fino que se resolvió como ADVERTENCIA NO BLOQUEANTE --
    /// la instalación sigue, pero el operador queda avisado de que el servicio puede fallar al
    /// arrancar. Verificado con permisos UNIX reales (no un stub que devuelve 0 siempre):
    /// 'privkey.pem' queda con permisos 000 (nadie más que root puede leerlo) -- 'stockapp', un
    /// usuario de sistema sin privilegios, realmente no puede leerlo dentro del contenedor.
    /// </summary>
    [Fact]
    public void ApiSchemeHttps_CertNoLegiblePorStockapp_EmiteAdvertenciaNoBloqueanteYSigue()
    {
        var env = EnvBase + """

            API_SCHEME=https
            API_PORT=8080
            API_HOSTNAME=stockapp.test.local
            Kestrel__Certificates__Default__Path=/certs/fullchain.pem
            Kestrel__Certificates__Default__KeyPath=/certs/privkey.pem
            """;
        var (exitCode, salida, _, _, _) = EjecutarInstall(env, CrearTarballValido(), conTestbin: true, conSudo: true,
            permisosFullchain: 0b110_100_100, permisosPrivkey: 0b000_000_000);

        Assert.True(exitCode == 0, $"Esperaba exit 0 (advertencia no bloqueante). Salida:\n{salida}");
        Assert.Contains("ADVERTENCIA", salida);
        Assert.Contains("no puede leer", salida);
        Assert.Contains("privkey.pem", salida);
    }

    // ---------- Camino feliz: la unit generada declara el esquema correcto ----------

    [Fact]
    public void CaminoFeliz_ApiSchemeHttp_LaUnitDeclaraHttpYElHealthcheckNoUsaResolve()
    {
        var env = EnvBase + "\nAPI_SCHEME=http\nAPI_PORT=8080\n";
        var (exitCode, salida, unit, rastroCurl, _) = EjecutarInstall(
            env, CrearTarballValido(), conTestbin: true);

        Assert.True(exitCode == 0, $"Esperaba exit 0. Salida:\n{salida}");
        Assert.Contains("Environment=ASPNETCORE_URLS=http://127.0.0.1:8080", unit);
        Assert.Contains("http://127.0.0.1:8080/licencia/estado", rastroCurl);
        Assert.DoesNotContain("--resolve", rastroCurl);
    }

    [Fact]
    public void CaminoFeliz_ApiSchemeHttps_LaUnitDeclaraHttpsYElHealthcheckUsaResolve()
    {
        var env = EnvBase + """

            API_SCHEME=https
            API_PORT=8080
            API_HOSTNAME=stockapp.test.local
            Kestrel__Certificates__Default__Path=/certs/fullchain.pem
            Kestrel__Certificates__Default__KeyPath=/certs/privkey.pem
            """;
        var (exitCode, salida, unit, rastroCurl, apiEnv) = EjecutarInstall(
            env, CrearTarballValido(), conTestbin: true, conSudo: true,
            permisosFullchain: 0b110_100_100, permisosPrivkey: 0b110_100_100);

        Assert.True(exitCode == 0, $"Esperaba exit 0. Salida:\n{salida}");
        Assert.Contains("Environment=ASPNETCORE_URLS=https://127.0.0.1:8080", unit);
        Assert.Contains("--resolve stockapp.test.local:8080:127.0.0.1", rastroCurl);
        Assert.Contains("https://stockapp.test.local:8080/licencia/estado", rastroCurl);

        // Passthrough explícito (no genérico) de los dos Kestrel__* hacia /etc/stockapp-api/api.env
        // -- si SOLO estuvieran en VARS_CONOCIDAS sin escritura explícita, el mecanismo de
        // passthrough genérico los saltearía y la API nunca vería las rutas del certificado que
        // install.sh acaba de validar.
        Assert.Contains("Kestrel__Certificates__Default__Path=/certs/fullchain.pem", apiEnv);
        Assert.Contains("Kestrel__Certificates__Default__KeyPath=/certs/privkey.pem", apiEnv);
    }
}
