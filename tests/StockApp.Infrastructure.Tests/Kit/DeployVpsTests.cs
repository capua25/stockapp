using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/deploy-vps.sh (Fase 6, plan 2026-09-12, líneas ~2805-2880).
///
/// A diferencia de los scripts de deploy/kit/ (que corren EN el servidor, como root), este
/// script corre en la MÁQUINA DEL OPERADOR y orquesta el deploy por SSH/SCP -- no necesita
/// root ni contenedor Ubuntu (mismo criterio que ArmarKitTests: nada de /etc ni /opt locales).
/// Se arma un repo falso (deploy/deploy-vps.sh real + install.sh/publish-api.sh/.env dummy,
/// con git real inicializado para poder probar la lógica de "copiar install.sh solo si
/// cambió") y se stubean 'ssh'/'scp' por PATH. 'docker' NUNCA se invoca localmente -- solo
/// aparece DENTRO del string de comando remoto que se le pasa a 'ssh', así que no hace falta
/// stubearlo.
///
/// El plan no prevé tests para esta fase (dice que todo se verifica a mano contra el VPS) --
/// estos SÍ existen, con la misma técnica que ya usamos 5 veces (repro ≠ guardián, convención
/// del proyecto).
///
/// El guardián más importante de esta suite es <see cref="DryRun_SoloCorreElPrevuelo_NoTocaNadaMas"/>:
/// con --dry-run, ni 'scp' ni el comando remoto 'install.sh' pueden dejar rastro.
/// </summary>
public class DeployVpsTests
{
    private static string RutaScriptReal([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy", "deploy-vps.sh"));
    }

    // ---------- Stubs de ssh/scp ----------

    /// <summary>
    /// ssh falso: registra cada invocación completa en $RASTRO_SSH (con %q) y despacha una
    /// respuesta según el CONTENIDO del comando remoto (último argumento) -- el mismo patrón de
    /// dispatch-por-contenido que VerificarTests.CurlFalso usa para el path de la URL.
    ///
    /// El chequeo de duplicados (paso1_prevuelo_migraciones) manda la SQL por la ENTRADA
    /// ESTÁNDAR (fix del BUG de quoting, ver deploy-vps.sh) -- por eso el dispatch para ese
    /// caso ya NO puede mirar el contenido del argv (ahí solo queda "docker exec -i
    /// stockapp-pg psql ... -tA", sin rastro de SQL): hay que leer stdin para decidir la
    /// respuesta, igual que haría el psql real.
    /// </summary>
    private static string SshFalso(bool simularDuplicados = false, bool simularFalloConsulta = false) => $$"""
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_SSH:-/dev/null}"

        args=("$@")
        cmd="${args[-1]}"

        case "$cmd" in
            *"docker exec -i stockapp-pg psql"*"-tA"*)
                entrada="$(cat)"
                case "$entrada" in
                    *"HAVING COUNT(*) > 1"*)
                        {{(simularFalloConsulta
                            ? "echo 'psql: FATAL: no se pudo conectar (simulado)' >&2; exit 3"
                            : simularDuplicados
                                ? "echo 'nombre duplicado|1, 2|2'; exit 0"
                                : ": # sin duplicados, sin salida; exit 0")}}
                        ;;
                    *)
                        exit 0
                        ;;
                esac
                ;;
            *"/auth/login"*)
                echo '{"token":"FAKE-TOKEN-123"}'
                exit 0
                ;;
            *"mktemp "*"stockapp-backup"*)
                # Ruta remota FIJA a propósito (no un mktemp real de verdad) -- así el test
                # puede afirmar sobre el nombre exacto que el script usa después para chmod/rm.
                echo "/tmp/stockapp-backup-FAKEID.bin"
                exit 0
                ;;
            *"-X POST"*"/backups"*)
                exit 0
                ;;
            *"curl -fsS http"*"/backups"*)
                # Contrato real (BackupDtos.CorridaBackupDto + camelCase por default de
                # ConfigureHttpJsonOptions): campos "finalizadaEn"/"resultado", valor "Exitosa"
                # (no "estado"/"Exitoso", que nunca existió). "finalizadaEn" se genera EN EL
                # MOMENTO en que corre el stub para que quede posterior al momento_disparo_epoch
                # que el script captura antes del POST -- así el camino feliz encuentra
                # exactamente 1 candidato sin tener que reintentar.
                ahora="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
                if [[ -n "${SIMULAR_BACKUP_VIEJO_PRIMERO:-}" ]]; then
                    # El backup VIEJO (id 1, finalizadaEn muy anterior) aparece PRIMERO en la
                    # lista y el RECIÉN CREADO (id 2, finalizadaEn = ahora) aparece SEGUNDO --
                    # a propósito, para probar que la selección no depende de la posición ni de
                    # confiar en un orden dado por el servidor, sino de comparar contra el
                    # momento real del disparo.
                    printf '[{"id":1,"finalizadaEn":"2020-01-01T00:00:00Z","resultado":"Exitosa","nombreArchivo":"viejo.bin","tamanioBytes":2048,"motivoFallo":null},{"id":2,"finalizadaEn":"%s","resultado":"Exitosa","nombreArchivo":"nuevo.bin","tamanioBytes":4096,"motivoFallo":null}]\n' "$ahora"
                else
                    printf '[{"id":42,"finalizadaEn":"%s","resultado":"Exitosa","nombreArchivo":"x.bin","tamanioBytes":2048,"motivoFallo":null}]\n' "$ahora"
                fi
                exit 0
                ;;
            *"__EFMigrationsHistory"*)
                echo "5"
                exit 0
                ;;
            *"licencia/estado"*)
                echo '{"activada":true,"codigoMaquina":"TEST-0000"}'
                exit 0
                ;;
            *"systemctl is-active stockapp-api"*)
                echo "active"
                exit 0
                ;;
            *"install.sh"*)
                echo "(stub) instalacion OK"
                exit 0
                ;;
            *)
                exit 0
                ;;
        esac
        """;

    /// <summary>
    /// scp falso: registra cada invocación en $RASTRO_SCP y, cuando el ÚLTIMO argumento es una
    /// ruta LOCAL (no tiene forma "usuario@host:ruta" -- una descarga, no una subida), crea ese
    /// archivo con $SCP_TAMANO_BYTES de contenido (default 2048, para simular un backup real no
    /// vacío; poner 0 simula el backup vacío que el script tiene que detectar).
    /// </summary>
    private const string ScpFalso = """
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_SCP:-/dev/null}"

        args=("$@")
        ultimo="${args[-1]}"
        if [[ "$ultimo" != *@*:* ]]; then
            tam="${SCP_TAMANO_BYTES:-2048}"
            if [[ "$tam" -gt 0 ]]; then
                head -c "$tam" /dev/zero > "$ultimo" 2>/dev/null
            else
                : > "$ultimo"
            fi
        fi
        exit 0
        """;

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static string CrearTestbin(bool simularDuplicados = false, bool simularFalloConsulta = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "deploy-vps-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        EscribirEjecutable(Path.Combine(dir, "ssh"), SshFalso(simularDuplicados, simularFalloConsulta));
        EscribirEjecutable(Path.Combine(dir, "scp"), ScpFalso);
        return dir;
    }

    // ---------- Repo falso con git real (para la lógica de "copiar install.sh solo si cambió") ----------

    /// <summary>
    /// publish-api.sh falso: deja rastro en $RASTRO_PUBLISH si se lo invoca, y se comporta como
    /// el real -- crea el tarball e imprime la línea "[publish-api] OK: &lt;ruta&gt;" que
    /// deploy-vps.sh parsea.
    /// </summary>
    private const string PublishApiFalso = """
        #!/usr/bin/env bash
        set -euo pipefail
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_PUBLISH:-/dev/null}"
        VERSION="${1:?}"
        DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/dist"
        mkdir -p "$DIR"
        TARBALL="${DIR}/stockapp-api-${VERSION}-linux-x64.tar.gz"
        echo "contenido-dummy" > "$TARBALL"
        echo "[publish-api] Publicando (stub)..."
        echo "[publish-api] OK: ${TARBALL}"
        """;

    private static string CrearRepoFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "deploy-vps-fixture-" + Guid.NewGuid());
        var deploy = Path.Combine(root, "deploy");
        Directory.CreateDirectory(deploy);

        EscribirEjecutable(Path.Combine(deploy, "install.sh"), "#!/usr/bin/env bash\nexit 0\n");
        File.WriteAllText(Path.Combine(deploy, "stockapp-api.service"), "[Unit]\nDescription=dummy\n");
        EscribirEjecutable(Path.Combine(deploy, "wait-for-postgres.sh"), "#!/usr/bin/env bash\nexit 0\n");
        EscribirEjecutable(Path.Combine(deploy, "publish-api.sh"), PublishApiFalso);
        File.WriteAllText(Path.Combine(deploy, ".env"), """
            POSTGRES_USER=stockapp
            POSTGRES_DB=stockapp
            POSTGRES_PASSWORD=secreta
            API_PORT=5080
            BOOTSTRAP_ADMIN_USER=admin
            BOOTSTRAP_PASSWORD=Admin12345
            """);

        var scriptReal = File.ReadAllText(RutaScriptReal());
        EscribirEjecutable(Path.Combine(deploy, "deploy-vps.sh"), scriptReal);

        var (exitInit, _, stderrInit) = EjecutorBash.Ejecutar(
            $"cd '{root}' && git init -q && git -c user.email=test@test.local -c user.name=Test add -A && " +
            "git -c user.email=test@test.local -c user.name=Test commit -q -m inicial");
        Assert.True(exitInit == 0, $"git init/commit falló en el repo fixture: {stderrInit}");

        return root;
    }

    /// <summary>Corre deploy/deploy-vps.sh del repo falso con ssh/scp stubeados por PATH.</summary>
    private static (int ExitCode, string Stdout, string Stderr, string RastroSsh, string RastroScp, string RastroPublish) Correr(
        string repoFixture, string argumentos, bool simularDuplicados = false,
        string scpTamanoBytes = "2048", string vpsUser = "operador",
        bool simularBackupViejoPrimero = false, bool simularFalloConsulta = false)
    {
        var testbin = CrearTestbin(simularDuplicados, simularFalloConsulta);
        var rastroSsh = Path.Combine(Path.GetTempPath(), "deploy-vps-rastro-ssh-" + Guid.NewGuid());
        var rastroScp = Path.Combine(Path.GetTempPath(), "deploy-vps-rastro-scp-" + Guid.NewGuid());
        var rastroPublish = Path.Combine(Path.GetTempPath(), "deploy-vps-rastro-publish-" + Guid.NewGuid());
        try
        {
            var script =
                $"export PATH=\"{testbin}:$PATH\"\n" +
                $"export RASTRO_SSH=\"{rastroSsh}\"\n" +
                $"export RASTRO_SCP=\"{rastroScp}\"\n" +
                $"export RASTRO_PUBLISH=\"{rastroPublish}\"\n" +
                $"export SCP_TAMANO_BYTES=\"{scpTamanoBytes}\"\n" +
                $"export VPS_USER=\"{vpsUser}\"\n" +
                (simularBackupViejoPrimero ? "export SIMULAR_BACKUP_VIEJO_PRIMERO=1\n" : "") +
                $"bash \"{repoFixture}/deploy/deploy-vps.sh\" {argumentos}";
            var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(script);
            var ssh = File.Exists(rastroSsh) ? File.ReadAllText(rastroSsh) : "";
            var scp = File.Exists(rastroScp) ? File.ReadAllText(rastroScp) : "";
            var publish = File.Exists(rastroPublish) ? File.ReadAllText(rastroPublish) : "";
            return (exitCode, stdout, stderr, ssh, scp, publish);
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
            if (File.Exists(rastroSsh)) File.Delete(rastroSsh);
            if (File.Exists(rastroScp)) File.Delete(rastroScp);
            if (File.Exists(rastroPublish)) File.Delete(rastroPublish);
        }
    }

    // ---------- Regla: sin <version> ----------

    [Fact]
    public void SinVersion_MuestraUsoYAborta()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, _, _, _) = Correr(repo, "");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("Uso:", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: --dry-run NO ejecuta nada más que el pre-vuelo (EL guardián principal) ----------

    [Fact]
    public void DryRun_SoloCorreElPrevuelo_NoTocaNadaMas()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, rastroScp, rastroPublish) =
                Correr(repo, "1.0.0 --dry-run");
            var salida = stdout + stderr;

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            // El pre-vuelo SÍ usa ssh (docker exec de solo lectura) -- lo que NO puede pasar es
            // que llegue a copiar nada (scp) ni a invocar install.sh en el remoto.
            Assert.DoesNotContain("install.sh", rastroSsh);
            Assert.Equal("", rastroScp);
            Assert.Equal("", rastroPublish);
            Assert.Contains("dry-run", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: --con-soporte no existe (Decisión 10) ----------

    [Fact]
    public void ConSoporte_EsRechazadoNoIgnoradoEnSilencio()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, _, _) = Correr(repo, "1.0.0 --con-soporte");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("--con-soporte", salida);
            // Prueba negativa de "ignorado en silencio": si lo hubiera ignorado, habría seguido
            // hasta el pre-vuelo y dejado rastro de ssh.
            Assert.Equal("", rastroSsh);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: duplicados en el pre-vuelo abortan y NUNCA ejecutan la remediación (Decisión 11) ----------

    [Fact]
    public void PrevueloConDuplicados_AbortaMostrandoRemediacionSinEjecutarla()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, rastroScp, _) =
                Correr(repo, "1.0.0", simularDuplicados: true);
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("duplicados", salida, StringComparison.OrdinalIgnoreCase);
            // Muestra un UPDATE sugerido...
            Assert.Contains("UPDATE", salida);
            // ...pero el ssh stub -- que dejaría rastro de CUALQUIER comando que se le pase, UPDATE
            // incluido -- nunca ve un UPDATE real en su rastro: la remediación se imprime, no se manda.
            Assert.DoesNotContain("UPDATE", rastroSsh);
            Assert.Equal("", rastroScp);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: si la consulta de duplicados FALLA, se ABORTA -- nunca se confunde con "sin duplicados" ----------

    /// <summary>
    /// EL GUARDIÁN ESTRELLA: BUG hallado corriendo --dry-run contra el VPS real (deploy-vps.sh,
    /// paso1_prevuelo_migraciones). La SQL de STRING_AGG contiene una comilla simple LITERAL
    /// (', ') que -- antes de este fix -- viajaba interpolada dentro de "docker exec ...
    /// -tAc '${sql}'"; el shell remoto la tomaba como cierre prematuro de comillas y partía el
    /// comando, psql fallaba con "syntax error" por STDERR, y el "|| true" de la versión vieja
    /// se tragaba el código de salida A CIEGAS. Como la consulta nunca corrió, su stdout
    /// quedaba vacío -- exactamente la MISMA señal que "consulté y no hay duplicados". El
    /// pre-vuelo reportaba "OK. Sin duplicados en los 7 catálogos" sin haber verificado nada.
    ///
    /// Este test simula esa falla (el stub de ssh sale con código != 0 y escribe en stderr,
    /// mismo contrato que un psql real que no pudo correr la consulta) y confirma que el
    /// script AHORA distingue "no pude verificar" de "verifiqué y no hay duplicados": aborta
    /// con un mensaje explícito, nunca con el falso "OK".
    /// </summary>
    [Fact]
    public void PrevueloFallaAlConsultar_AbortaSinConfundirloConSinDuplicados()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, rastroScp, rastroPublish) =
                Correr(repo, "1.0.0", simularFalloConsulta: true);
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("no se pudo verificar", salida, StringComparison.OrdinalIgnoreCase);
            // El mensaje tiene que dejar explícito que un fallo de consulta NO ES "sin
            // duplicados" -- si esta afirmación fallara, sería la MISMA confusión del bug real.
            Assert.DoesNotContain("OK. Sin duplicados", salida);
            // Nunca llega a copiar artefactos ni a publicar: abortó en el paso 1.
            Assert.Equal("", rastroScp);
            Assert.Equal("", rastroPublish);
            Assert.Contains("docker exec -i stockapp-pg psql", rastroSsh.Replace("\\ ", " "));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: la SQL del pre-vuelo viaja por stdin, nunca por argv (guardián del quoting) ----------

    /// <summary>
    /// ssh falso que hace EXACTAMENTE lo que un shell remoto real hace: recibe el comando como
    /// UN SOLO argv (el que 'ssh_vps "$cmd_remoto"' le manda) y lo vuelve a tokenizar con
    /// 'eval' -- así fue como se reprodujo el bug de quoting original la primera vez, sin tocar
    /// el VPS real. Los demás tests de esta clase usan dispatch por substring (nunca reparsean
    /// nada), así que no podían detectar este bug -- este stub sí.
    /// </summary>
    private const string SshReparseoRealFalso = """
        #!/usr/bin/env bash
        args=("$@")
        cmd="${args[-1]}"
        eval "$cmd"
        """;

    /// <summary>
    /// docker falso para el test de reparseo: registra en $RASTRO_DOCKER la CANTIDAD exacta de
    /// argumentos que recibió (ARGV(n):...) y, si '-i' está entre ellos (docker exec -i, la
    /// forma nueva que lee de stdin), también el contenido crudo de su entrada estándar. No
    /// hace falta comportarse como psql de verdad -- alcanza con probar que ni la CANTIDAD de
    /// argumentos ni el contenido de stdin se corrompen al pasar por el reparseo.
    /// </summary>
    private const string DockerFalso = """
        #!/usr/bin/env bash
        {
            printf 'ARGV(%s):' "$#"
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_DOCKER:?}"

        tiene_dash_i=0
        for a in "$@"; do
            [[ "$a" == "-i" ]] && tiene_dash_i=1
        done
        if [[ "$tiene_dash_i" -eq 1 ]]; then
            {
                printf 'STDIN:'
                cat
                printf '\n'
            } >> "${RASTRO_DOCKER:?}"
        fi
        exit 0
        """;

    /// <summary>sudo/systemctl falsos: no-ops para que paso0 (docker ps/systemctl/sudo ss, todo
    /// no bloqueante) no dispare el 'sudo' o 'systemctl' REAL de la máquina de test al pasar
    /// por 'eval'.</summary>
    private const string SudoFalso = "#!/usr/bin/env bash\nexit 1\n";
    private const string SystemctlFalso = "#!/usr/bin/env bash\necho inactive\nexit 1\n";

    [Fact]
    public void Prevuelo_LaSqlLlegaIntactaPorStdin_AunqueElShellRemotoLaReparse()
    {
        var repo = CrearRepoFixture();
        var testbin = Path.Combine(Path.GetTempPath(), "deploy-vps-testbin-reparseo-" + Guid.NewGuid());
        Directory.CreateDirectory(testbin);
        EscribirEjecutable(Path.Combine(testbin, "ssh"), SshReparseoRealFalso);
        EscribirEjecutable(Path.Combine(testbin, "scp"), ScpFalso);
        EscribirEjecutable(Path.Combine(testbin, "docker"), DockerFalso);
        EscribirEjecutable(Path.Combine(testbin, "sudo"), SudoFalso);
        EscribirEjecutable(Path.Combine(testbin, "systemctl"), SystemctlFalso);
        var rastroDocker = Path.Combine(Path.GetTempPath(), "deploy-vps-rastro-docker-" + Guid.NewGuid());
        try
        {
            var script =
                $"export PATH=\"{testbin}:$PATH\"\n" +
                $"export RASTRO_DOCKER=\"{rastroDocker}\"\n" +
                "export VPS_USER=\"operador\"\n" +
                $"bash \"{repo}/deploy/deploy-vps.sh\" 1.0.0 --dry-run";
            var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(script);
            var docker = File.Exists(rastroDocker) ? File.ReadAllText(rastroDocker) : "";

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}\ndocker={docker}");

            // 'docker exec -i stockapp-pg psql -U ... -d ... -h 127.0.0.1 -tA' son EXACTAMENTE
            // 11 tokens. El bug original corrompía esto: con la SQL interpolada en '-tAc',
            // docker recibía 12 argumentos (el -tAc truncado por la comilla de STRING_AGG más
            // un fragmento suelto de la SQL como argumento extra).
            var lineasArgvPsql = docker.Split('\n')
                .Where(l => l.StartsWith("ARGV(") && l.Contains("-tA")).ToList();
            Assert.True(lineasArgvPsql.Count == 7,
                $"Se esperaban 7 invocaciones a psql (una por catálogo), hubo {lineasArgvPsql.Count}:\n{docker}");
            Assert.All(lineasArgvPsql, l => Assert.StartsWith("ARGV(11):", l));
            // La SQL NUNCA aparece en el argv -- solo pudo llegar por stdin.
            Assert.All(lineasArgvPsql, l => Assert.DoesNotContain("STRING_AGG", l));

            // Y por stdin llega INTACTA -- comilla simple de STRING_AGG incluida, sin truncar.
            Assert.Contains(
                "STDIN:SELECT LOWER(\"Nombre\") AS nombre_normalizado, STRING_AGG(\"Id\"::text, ', ' ORDER BY \"Id\") AS ids, COUNT(*) AS cantidad FROM \"Categorias\" GROUP BY LOWER(\"Nombre\") HAVING COUNT(*) > 1;",
                docker);
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
            Directory.Delete(testbin, recursive: true);
            if (File.Exists(rastroDocker)) File.Delete(rastroDocker);
        }
    }

    // ---------- Regla: backup vacío/0 bytes aborta (no confía en el exit code de scp) ----------

    [Fact]
    public void BackupDeCeroBytes_Aborta()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, _, rastroScp, rastroPublish) =
                Correr(repo, "1.0.0", scpTamanoBytes: "0");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("backup", salida, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual("", rastroScp);       // sí intentó bajarlo...
            Assert.Equal("", rastroPublish);      // ...pero abortó ANTES de publicar/instalar.
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: --sin-backup imprime advertencia llamativa y saltea el backup ----------

    [Fact]
    public void SinBackup_ImprimeAdvertenciaLlamativaYSalteaElBackup()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, _, rastroScp, rastroPublish) =
                Correr(repo, "1.0.0 --sin-backup");
            var salida = stdout + stderr;

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.Contains("ADVERTENCIA", salida);
            // Cita del diseño (línea 135): un script que por omisión deja sin punto de retorno
            // está mal diseñado -- confirma que el mensaje explica POR QUÉ el default es con backup.
            Assert.Contains("sin punto de retorno", salida);
            Assert.DoesNotContain("scp", rastroScp.Replace("ARGS: scp", ""));
            // El flujo sigue: publish-api.sh sí se invoca (paso 3 no depende del backup).
            Assert.NotEqual("", rastroPublish);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: el tarball se pasa por nombre exacto, nunca un glob ----------

    [Fact]
    public void InstalacionRemota_UsaElNombreExactoDelTarballNuncaUnGlob()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, _, rastroPublish) = Correr(repo, "9.9.9");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.Contains("ARGS: 9.9.9", rastroPublish);
            Assert.Contains("stockapp-api-9.9.9-linux-x64.tar.gz", stderr);

            var lineaInstall = rastroSsh.Split('\n').FirstOrDefault(l => l.Contains("install.sh"));
            Assert.False(string.IsNullOrEmpty(lineaInstall), $"No se encontró la invocación a install.sh en:\n{rastroSsh}");
            Assert.Contains("stockapp-api-9.9.9-linux-x64.tar.gz", lineaInstall);
            Assert.DoesNotContain("*", lineaInstall);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: ningún comando prohibido en el script (custodia el VPS compartido) ----------

    /// <summary>
    /// Estático, no de ejecución: lee el TEXTO del script y afirma que nunca contiene (ni
    /// siquiera comentado con intención de ilustrar, salvo en un comentario que EXPLÍCITAMENTE
    /// diga "PROHIBIDO") ninguno de los comandos de alcance global prohibidos para este VPS
    /// compartido. Si alguien los agrega en el futuro, este test se pone rojo.
    /// </summary>
    [Fact]
    public void ElScript_NuncaContieneUnComandoDeAlcanceGlobalProhibido()
    {
        var texto = File.ReadAllText(RutaScriptReal());

        Assert.DoesNotContain("docker system prune", texto);
        Assert.DoesNotContain("image prune", texto);
        Assert.DoesNotContain("volume prune", texto);
        Assert.DoesNotContain("container prune", texto);
        Assert.DoesNotContain("systemctl restart docker", texto);
        Assert.DoesNotContain("systemctl stop docker", texto);
        Assert.DoesNotContain("ufw", texto);
        Assert.DoesNotContain("apt-get upgrade", texto);
        Assert.DoesNotContain("dist-upgrade", texto);
        Assert.DoesNotContain("autoremove", texto);
        Assert.DoesNotContain("reboot", texto);
        Assert.DoesNotContain("shutdown", texto);
        Assert.DoesNotMatch(@"docker\s+stop\s+\$\(", texto);
        Assert.DoesNotContain("pkill", texto);
        // docker compose down sin -f/-p explícito
        Assert.DoesNotMatch(@"docker compose down(?!.*(-f|-p))", texto);
    }

    // ---------- Camino feliz completo: llega al paso 6 y verifica ----------

    [Fact]
    public void CaminoFeliz_LlegaHastaLaVerificacionRemotaYTerminaOk()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, rastroScp, rastroPublish) = Correr(repo, "1.2.3");
            var salida = stdout + stderr;

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.Contains("ARGS: 1.2.3", rastroPublish);
            Assert.Contains("stockapp-api-1.2.3-linux-x64.tar.gz", stderr);
            Assert.Contains("licencia/estado", rastroSsh);
            Assert.Contains("__EFMigrationsHistory", rastroSsh);
            // %q escapa los espacios con '\ ' al volcar el rastro -- normalizamos antes de
            // buscar una substring con espacios.
            Assert.Contains("systemctl is-active stockapp-api", rastroSsh.Replace("\\ ", " "));
            Assert.NotEqual("", rastroScp);
            Assert.Contains("VPS COMPARTIDO", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: el backup bajado es el RECIÉN CREADO, no el primero exitoso de la lista ----------

    /// <summary>
    /// BUG 1 (deploy/deploy-vps.sh:303-309, hallado por revisión independiente): POST /backups
    /// (BackupsEndpoints.cs:49-59) responde 202 Accepted SIN body -- no hay id que capturar de
    /// ahí. El script anterior confiaba en "head -1" sobre TODA la lista de GET /backups
    /// filtrada por exitosos, asumiendo que el servidor la devuelve más-nuevo-primero. Con
    /// BackupProgramadoService corriendo backups automáticos en paralelo, esa asunción de orden
    /// no es una garantía de la que dependa la selección del punto de retorno del deploy.
    ///
    /// Este guardián arma una lista adversarial -- el backup VIEJO (id 1) aparece PRIMERO y el
    /// RECIÉN CREADO (id 2, finalizadaEn posterior al momento del disparo) aparece SEGUNDO -- y
    /// verifica que el script descargue el id 2, nunca el id 1, sin importar el orden en que
    /// vinieron.
    /// </summary>
    [Fact]
    public void PasoBackup_BajaElBackupRecienCreado_NoElPrimeroExitosoDeLaLista()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, _, _) =
                Correr(repo, "1.2.3", simularBackupViejoPrimero: true);
            var salida = stdout + stderr;
            var ssh = rastroSsh.Replace("\\ ", " ");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.Contains("/backups/2/contenido", ssh);
            Assert.DoesNotContain("/backups/1/contenido", ssh);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: el dump remoto usa mktemp y se borra siempre (BUG 2, VPS compartido) ----------

    /// <summary>
    /// BUG 2 (deploy/deploy-vps.sh:319, hallado por revisión independiente): la ruta remota del
    /// dump pre-deploy era un nombre FIJO y predecible ("/tmp/stockapp-backup-pre-deploy-VERSION.bin")
    /// y nunca se borraba -- un dump completo de la base del municipio quedaba legible en el /tmp
    /// de un VPS COMPARTIDO con "pinar" indefinidamente. Estático: el script no puede volver a
    /// tener una ruta fija bajo /tmp para el backup, y tiene que usar mktemp remoto.
    /// </summary>
    [Fact]
    public void ElScript_NoUsaUnaRutaFijaEnTmpParaElDumpDeBackup()
    {
        var texto = File.ReadAllText(RutaScriptReal());

        Assert.DoesNotMatch(@"/tmp/stockapp-backup-pre-deploy-\$\{VERSION\}", texto);
        Assert.Contains("mktemp", texto);
    }

    /// <summary>
    /// Dinámico (guardián fuerte): en el camino feliz, el script tiene que (1) crear la ruta
    /// remota con mktemp (nunca un nombre armado a mano), (2) restringir permisos con chmod 600
    /// ANTES de escribirle el dump, y (3) borrarla al terminar -- las tres cosas verificadas
    /// contra el rastro REAL de comandos que le llegaron a 'ssh' (no contra el texto del script).
    /// </summary>
    [Fact]
    public void PasoBackup_CreaElRemotoConMktempLoRestringeYLoBorraAlTerminar()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, _, _) = Correr(repo, "1.2.3");
            var ssh = rastroSsh.Replace("\\ ", " ").Replace("\\'", "'");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.Contains("mktemp /tmp/stockapp-backup-", ssh);
            Assert.Contains("chmod 600 '/tmp/stockapp-backup-FAKEID.bin'", ssh);
            Assert.Contains("rm -f '/tmp/stockapp-backup-FAKEID.bin'", ssh);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    /// <summary>
    /// El caso que de verdad importa: si el deploy ABORTA a mitad de camino DESPUÉS de haber
    /// creado el dump remoto (acá, el mismo escenario que <see cref="BackupDeCeroBytes_Aborta"/> --
    /// el backup bajado pesa 0 bytes), el archivo remoto se tiene que borrar IGUAL. Si no hay un
    /// trap de limpieza (solo un 'rm' al final del camino feliz), este es justo el caso que se
    /// escapa: el dump de producción queda abandonado en el VPS compartido.
    /// </summary>
    [Fact]
    public void PasoBackup_SiElDeployAbortaDespuesDeCrearElRemoto_IgualLoBorra()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, _, _) =
                Correr(repo, "1.2.3", scpTamanoBytes: "0");
            var ssh = rastroSsh.Replace("\\ ", " ").Replace("\\'", "'");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("mktemp /tmp/stockapp-backup-", ssh);
            Assert.Contains("rm -f '/tmp/stockapp-backup-FAKEID.bin'", ssh);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Regla: ningún secreto viaja por argv (hallazgo de seguridad, VPS compartido) ----------

    /// <summary>
    /// Estático: estos dos patrones son la forma EXACTA del hallazgo original ('-H
    /// "Authorization: Bearer $token"' y '-d "{...contrasena...}"' embebidos en el string de
    /// comando remoto, es decir en argv). Si alguien los reintroduce, este test se pone rojo
    /// antes de que el dinámico de abajo siquiera corra.
    /// </summary>
    [Fact]
    public void ElScript_NuncaInterpolaElHeaderDeAuthOrizacionNiElBodyDeLoginEnArgv()
    {
        var texto = File.ReadAllText(RutaScriptReal());

        Assert.DoesNotContain("-H 'Authorization: Bearer", texto);
        Assert.DoesNotMatch(@"-d '\{[^']*contrasena", texto);
        // Sí tiene que usar los mecanismos por stdin que reemplazan a los de arriba.
        Assert.Contains("--data @-", texto);
        Assert.Contains("-K -", texto);
    }

    /// <summary>
    /// Dinámico (el guardián FUERTE, no solo estático): corre el camino feliz completo -- que
    /// SÍ pasa por login y por los tres llamados autenticados -- y afirma que ni la contraseña
    /// real del admin ("Admin12345", la del .env del fixture) ni el token real que devuelve el
    /// stub ("FAKE-TOKEN-123") aparecen JAMÁS en el rastro de argv de 'ssh' (rastroSsh solo
    /// registra "$@" -- lo que curl/ssh reciben por STDIN, como el nuevo body/header, no queda
    /// ahí). Si alguien reintroduce el secreto en argv -- aunque sea con otra sintaxis que el
    /// test estático de arriba no anticipe -- este test lo agarra igual.
    /// </summary>
    [Fact]
    public void CaminoFeliz_NingunSecretoApareceEnElArgvDeSsh()
    {
        var repo = CrearRepoFixture();
        try
        {
            var (exitCode, stdout, stderr, rastroSsh, _, _) = Correr(repo, "1.2.3");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.DoesNotContain("Admin12345", rastroSsh);
            Assert.DoesNotContain("FAKE-TOKEN-123", rastroSsh);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }
}
