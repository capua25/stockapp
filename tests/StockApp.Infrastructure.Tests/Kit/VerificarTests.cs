using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/kit/03-verificar.sh (Task 3.3, plan 2026-09-12, líneas ~1927-2172).
///
/// El script necesita root porque lee /etc/stockapp/.env (600, dueño root) -- misma guardia que
/// 02-instalar.sh y 04-licencia.sh (LicenciaTests.cs, InstalarTests.cs). El resto de los 8
/// chequeos necesita systemctl/curl/psql/ip reales, que no existen (o no dan el resultado
/// determinístico que un test necesita) en la máquina que corre la suite. Se stubean esos
/// cuatro binarios en un directorio descartable antepuesto al PATH, DENTRO de un contenedor
/// Ubuntu (mismo patrón que PreflightTests/LicenciaTests): así el script corre como root de
/// verdad sin tocar el sistema real, y "todo en verde" es reproducible sin depender de que la
/// API, Postgres o la red de la máquina de test estén en un estado particular.
///
/// El corazón de esta suite es el Chequeo 2 (Corrección 4 del plan): el script tiene que
/// asertar el VALOR de ASPNETCORE_URLS, no solo que la línea exista. El test
/// Chequeo2_ValorDePuertoEquivocado_EsBloqueante monta una unit con la línea PRESENTE pero con
/// el puerto equivocado -- una aserción laxa ("la línea existe") pasaría igual ahí. Verificado
/// por mutación (ver reporte de la Task 3.3).
///
/// Shape del login verificado contra src/StockApp.Api/Endpoints/AuthEndpoints.cs:11 y
/// deploy/DEPLOY.md:345-347: los campos son "nombreUsuario"/"contrasena" (no "usuario"/
/// "password", como asumía el borrador del plan). El fake de curl de esta suite no valida el
/// cuerpo del POST (sería probar el fake, no el script); lo que se prueba es que el script
/// reacciona bien al HTTP status y al campo "token" de la respuesta.
/// </summary>
public class VerificarTests
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
            "deploy", "kit", "03-verificar.sh"));
    }

    private static string VersionUbuntuDelKit()
    {
        var version = File.ReadAllText(Path.Combine(RutaKit(), "VERSION"));
        return version.Trim();
    }

    // ---------- Guardián de root (sin docker: corre directo con el usuario no-root de la suite) ----------

    [Fact]
    public void SinRoot_AbortaConMensajeClaro()
    {
        var script = RutaScript();
        Assert.True(File.Exists(script), $"No se encontró el script en: {script}");

        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar($"bash '{script}'");
        var salida = stdout + stderr;

        Assert.NotEqual(0, exitCode);
        Assert.Contains("necesita root", salida);
    }

    // ---------- Entorno completo dentro de un contenedor Ubuntu descartable ----------

    /// <summary>
    /// Valores fijos del "entorno feliz": los stubs de curl/systemctl/psql/ip de más abajo
    /// están todos calibrados contra estos mismos valores.
    /// </summary>
    private const string ApiPort = "8080";
    private const string ApiBind = "0.0.0.0";
    private const string IpLan = "10.77.0.5";
    private const string Token = "FAKE-TOKEN-123";

    private static string EnvFeliz(string? apiScheme = null) => $"""
        API_PORT={ApiPort}
        API_BIND={ApiBind}
        BOOTSTRAP_ADMIN_USER=admin
        BOOTSTRAP_PASSWORD=Admin12345
        POSTGRES_USER=stockapp
        POSTGRES_DB=stockapp
        POSTGRES_PASSWORD=secreta
        POSTGRES_PORT=5433
        {(apiScheme is null ? "" : $"API_SCHEME={apiScheme}")}
        """;

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>systemctl falso: is-active (activo/inactivo, configurable) y cat (imprime una
    /// unit con la línea ASPNETCORE_URLS que se le pase, para poder mutar el valor a propósito
    /// en el Chequeo 2).</summary>
    private static string SystemctlFalso(string aspnetcoreUrls, bool servicioActivo = true)
    {
        var estado = servicioActivo ? "active" : "inactive";
        var exit = servicioActivo ? 0 : 3;
        return $"""
            #!/usr/bin/env bash
            case "$1" in
                is-active)
                    echo "{estado}"
                    exit {exit}
                    ;;
                cat)
                    cat <<'UNIT'
            [Unit]
            Description=StockApp API
            [Service]
            Environment=ASPNETCORE_URLS={aspnetcoreUrls}
            Environment=OTRA_VAR=algo
            UNIT
                    exit 0
                    ;;
                *) exit 0 ;;
            esac
            """;
    }

    /// <summary>ip falso: éxito (imprime "src {IpLan}", camino feliz) o fallo tipo "Network is
    /// unreachable" (mismo modo de falla que PreflightTests.CrearIpFalloSinRuta).</summary>
    private static string IpFalsoConRuta() => $"""
        #!/usr/bin/env bash
        echo "1.1.1.1 via {IpLan}0 dev eth0 src {IpLan} uid 0"
        exit 0
        """;

    private const string IpFalsoSinRuta = """
        #!/usr/bin/env bash
        echo 'RTNETLINK answers: Network is unreachable' >&2
        exit 2
        """;

    /// <summary>psql falso: distingue la consulta de conteo de la de "última migración" mirando
    /// el último argumento (la sentencia SQL que sigue a -tAc).</summary>
    private const string PsqlFalso = """
        #!/usr/bin/env bash
        sql="${*: -1}"
        if [[ "$sql" == *"count(*)"* ]]; then
            echo "3"
        elif [[ "$sql" == *"MigrationId"* ]]; then
            echo "20260101000000_UltimaMigracion"
        fi
        exit 0
        """;

    /// <summary>
    /// curl falso: dispatch por el path de la URL. /auth/login responde según
    /// <paramref name="httpLogin"/> (200 con token, o el código que se le pase -- "000" simula
    /// que el POST no respondió, sin disparar el fallback interactivo del script, que solo se
    /// activa con "401").
    /// </summary>
    private static string CurlFalso(string httpLogin = "200")
    {
        var cuerpoLogin = httpLogin == "200"
            ? $$"""
              if [[ -n "$outfile" ]]; then
                  printf '%s' '{"token":"{{Token}}"}' > "$outfile"
              fi
              printf '%s' '200'
              """
            : $$"""
              [[ -n "$outfile" ]] && : > "$outfile"
              printf '%s' '{{httpLogin}}'
              """;

        return $$"""
            #!/usr/bin/env bash
            args=("$@")
            url=""
            outfile=""
            for ((i=0; i<${#args[@]}; i++)); do
                case "${args[$i]}" in
                    -o) i=$((i+1)); outfile="${args[$i]}" ;;
                    http://*|https://*) url="${args[$i]}" ;;
                esac
            done

            case "$url" in
                */auth/login)
                    {{cuerpoLogin}}
                    exit 0
                    ;;
                */licencia/estado)
                    echo '{"activada":true,"codigoMaquina":"TEST-0000"}'
                    exit 0
                    ;;
                */backups/salud)
                    echo '{"ultimoExitoEn":"2026-09-14T03:00:00Z","vencido":false,"umbralHoras":26}'
                    exit 0
                    ;;
                *)
                    exit 7
                    ;;
            esac
            """;
    }

    /// <summary>
    /// Arma un directorio descartable con los 4 stubs (systemctl/curl/psql/ip) y lo monta junto
    /// con /kit en un contenedor Ubuntu efímero que corre 03-verificar.sh como root, con
    /// /etc/stockapp/.env ya escrito. Devuelve exit code real del script (vía marcador EXIT=,
    /// igual que LicenciaTests/InstalarTests) y la salida combinada.
    /// </summary>
    private static (int ExitCode, string Salida) EjecutarVerificar(
        string aspnetcoreUrls, string httpLogin = "200", bool servicioActivo = true,
        bool conRutaDeRed = true, string? envContent = null)
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();

        var dirTestbin = Path.Combine(Path.GetTempPath(), "verificar-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dirTestbin);
        try
        {
            EscribirEjecutable(Path.Combine(dirTestbin, "systemctl"),
                SystemctlFalso(aspnetcoreUrls, servicioActivo));
            EscribirEjecutable(Path.Combine(dirTestbin, "curl"), CurlFalso(httpLogin));
            EscribirEjecutable(Path.Combine(dirTestbin, "psql"), PsqlFalso);
            EscribirEjecutable(Path.Combine(dirTestbin, "ip"),
                conRutaDeRed ? IpFalsoConRuta() : IpFalsoSinRuta);

            var dirEnv = Path.Combine(Path.GetTempPath(), "verificar-env-" + Guid.NewGuid());
            Directory.CreateDirectory(dirEnv);
            try
            {
                File.WriteAllText(Path.Combine(dirEnv, ".env"), envContent ?? EnvFeliz());

                var plantilla = """
                    docker run --rm -v "__DIR_KIT__:/kit:ro" -v "__DIR_TESTBIN__:/testbin:ro" -v "__DIR_ENV__/.env:/etc/stockapp/.env:ro" "ubuntu:__UBUNTU__" bash -c '
                      export PATH="/testbin:$PATH"
                      /kit/03-verificar.sh 2>&1 | tee /tmp/salida
                      echo "EXIT=${PIPESTATUS[0]}"'
                    """;
                var script = plantilla
                    .Replace("__DIR_KIT__", dirKit)
                    .Replace("__DIR_TESTBIN__", dirTestbin)
                    .Replace("__DIR_ENV__", dirEnv)
                    .Replace("__UBUNTU__", ubuntu);

                var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(script);
                Assert.True(exitCodeDocker == 0, $"'docker run' falló. stdout={stdout} stderr={stderr}");

                var match = System.Text.RegularExpressions.Regex.Match(stdout, @"EXIT=(\d+)\s*$");
                Assert.True(match.Success, $"No se encontró el marcador EXIT= en la salida: {stdout}");
                return (int.Parse(match.Groups[1].Value), stdout);
            }
            finally
            {
                Directory.Delete(dirEnv, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(dirTestbin, recursive: true);
        }
    }

    private static string UrlEsperada(string esquema = "http") => $"ASPNETCORE_URLS={esquema}://{ApiBind}:{ApiPort}";

    // ---------- Guardián: sin /etc/stockapp/.env ----------

    [Fact]
    public void SinEnvDeServidor_AbortaConMensajeClaro()
    {
        var dirKit = RutaKit();
        var ubuntu = VersionUbuntuDelKit();

        var plantilla = """
            docker run --rm -v "__DIR_KIT__:/kit:ro" "ubuntu:__UBUNTU__" bash -c '
              /kit/03-verificar.sh 2>&1 | tee /tmp/salida
              echo "EXIT=${PIPESTATUS[0]}"'
            """;
        var script = plantilla.Replace("__DIR_KIT__", dirKit).Replace("__UBUNTU__", ubuntu);

        var (exitCodeDocker, stdout, stderr) = EjecutorBash.Ejecutar(script);
        Assert.True(exitCodeDocker == 0, $"'docker run' falló. stdout={stdout} stderr={stderr}");

        Assert.Contains("EXIT=1", stdout);
        // Texto único de ESTA guardia (la primera del script, antes de leer nada del .env):
        // no lo comparte ninguna otra guardia de 03-verificar.sh.
        Assert.Contains("No existe /etc/stockapp/.env", stdout);
    }

    // ---------- Chequeo 2: la corrección factual del plan (asertar VALOR, no presencia) ----------

    [Fact]
    public void Chequeo2_ValorCorrecto_Pasa()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada());

        Assert.DoesNotContain("La unit NO declara", salida);
        Assert.Contains($"La unit declara {UrlEsperada()}.", salida);
    }

    /// <summary>
    /// EL test que custodia la Corrección 4 del plan: la unit declara ASPNETCORE_URLS con un
    /// PUERTO DISTINTO del esperado -- la línea EXISTE (no es el caso "ninguna línea"), solo el
    /// valor está mal. Una aserción laxa que solo comprobara "la línea está" pasaría este
    /// escenario igual. El chequeo tiene que ser BLOQUEANTE.
    /// </summary>
    [Fact]
    public void Chequeo2_ValorDePuertoEquivocado_EsBloqueante()
    {
        var urlConPuertoEquivocado = $"http://{ApiBind}:9999";
        var (exitCode, salida) = EjecutarVerificar(urlConPuertoEquivocado);

        Assert.Contains($"La unit NO declara '{UrlEsperada()}'.", salida);
        Assert.Contains("CHEQUEO(S) FALLARON", salida);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Chequeo2_ValorDeBindEquivocado_EsBloqueante()
    {
        var urlConBindEquivocado = $"http://127.0.0.1:{ApiPort}";
        var (exitCode, salida) = EjecutarVerificar(urlConBindEquivocado);

        Assert.Contains($"La unit NO declara '{UrlEsperada()}'.", salida);
        Assert.Equal(1, exitCode);
    }

    /// <summary>
    /// API_SCHEME (2026-09-17): si alguna vez /etc/stockapp/.env llegara a declarar
    /// API_SCHEME=https (el kit municipal nunca lo hace hoy -- 01-bootstrap.sh se queda en
    /// http), 03-verificar.sh tiene que comparar contra ESE esquema, no contra "http" fijo.
    /// </summary>
    [Fact]
    public void Chequeo2_ApiSchemeHttps_ComparaConHttpsYPasa()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada("https"), envContent: EnvFeliz("https"));

        Assert.DoesNotContain("La unit NO declara", salida);
        Assert.Contains($"La unit declara {UrlEsperada("https")}.", salida);
    }

    /// <summary>
    /// El caso inverso: .env dice API_SCHEME=https pero la unit instalada todavía declara
    /// http (p.ej. quedó de una instalación anterior con install.sh viejo) -- el Chequeo 2
    /// tiene que detectarlo como un desajuste real, no darlo por bueno.
    /// </summary>
    [Fact]
    public void Chequeo2_ApiSchemeHttpsPeroUnitDeclaraHttp_EsBloqueante()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada("http"), envContent: EnvFeliz("https"));

        Assert.Contains($"La unit NO declara '{UrlEsperada("https")}'.", salida);
        Assert.Equal(1, exitCode);
    }

    // ---------- Chequeo 7: depende explícitamente del chequeo 6 ----------

    /// <summary>
    /// Si el login del chequeo 6 no consigue token (acá: la API "no respondió", HTTP=000 --
    /// evita el fallback interactivo de "read -rp", que solo dispara con 401), el chequeo 7
    /// tiene que fallar por DEPENDENCIA EXPLÍCITA, con su propio mensaje, no intentar el curl
    /// con un token vacío.
    /// </summary>
    [Fact]
    public void Chequeo7_SinTokenDelChequeo6_FallaPorDependenciaExplicita()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada(), httpLogin: "000");

        Assert.Contains("La API no respondió al login.", salida);
        Assert.Contains("No se pudo verificar: hace falta el login del chequeo 6.", salida);
        Assert.Equal(1, exitCode);
    }

    // ---------- Exit code final: 0 con todo OK, 1 con al menos un fallo ----------

    [Fact]
    public void TodoOk_LosOchoChequeosPasanYElExitEsCero()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada());

        Assert.Contains("LOS 8 CHEQUEOS PASARON.", salida);
        Assert.Contains($"http://{IpLan}:{ApiPort}", salida);
        Assert.Contains("COPIATE", salida);
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ServicioInactivo_HayAlMenosUnFalloYElExitEsUno()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada(), servicioActivo: false);

        Assert.Contains("stockapp-api NO está activo", salida);
        Assert.Contains("CHEQUEO(S) FALLARON", salida);
        Assert.Equal(1, exitCode);
    }

    // ---------- Gotcha de pipefail: sin ruta de red, el script IGUAL llega al final ----------

    /// <summary>
    /// El script del plan calculaba IP_LAN con 'ip route get | awk | head -1' SIN '|| true' al
    /// final del pipe -- bajo 'set -o pipefail', si el servidor no tiene ruta de red, 'ip' sale
    /// con exit 2 y el pipeline entero aborta TODO el script bajo 'set -e', antes de correr un
    /// solo chequeo. Mismo gotcha, mismo fix, que 00-preflight.sh (PreflightTests.
    /// Preflight_ServidorSinRutaDeRed_IgualLlegaAlFinalEImprimeElFingerprint). Acá se verifica
    /// que 03-verificar.sh llega al cierre igual, con el Chequeo 4 fallando explícitamente por
    /// IP_LAN vacía (no un crash silencioso del script entero).
    /// </summary>
    [Fact]
    public void ServidorSinRutaDeRed_IgualLlegaAlFinalYElChequeo4FallaExplicitamente()
    {
        var (exitCode, salida) = EjecutarVerificar(UrlEsperada(), conRutaDeRed: false);

        Assert.Contains("No pude determinar la IP de la LAN de este servidor.", salida);
        Assert.Contains("CHEQUEO(S) FALLARON", salida);
        Assert.Equal(1, exitCode);
    }
}
