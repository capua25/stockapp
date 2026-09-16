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
    /// </summary>
    private static string SshFalso(bool simularDuplicados = false) => $$"""
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_SSH:-/dev/null}"

        args=("$@")
        cmd="${args[-1]}"

        case "$cmd" in
            *"HAVING COUNT(*) > 1"*)
                {{(simularDuplicados
                    ? "echo 'nombre duplicado|1, 2|2'"
                    : ": # sin duplicados, sin salida")}}
                exit 0
                ;;
            *"/auth/login"*)
                echo '{"token":"FAKE-TOKEN-123"}'
                exit 0
                ;;
            *"-X POST"*"/backups"*)
                exit 0
                ;;
            *"curl -fsS http"*"/backups"*)
                echo '[{"id":42,"estado":"Exitoso"}]'
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

    private static string CrearTestbin(bool simularDuplicados = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "deploy-vps-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        EscribirEjecutable(Path.Combine(dir, "ssh"), SshFalso(simularDuplicados));
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
        string scpTamanoBytes = "2048", string vpsUser = "operador")
    {
        var testbin = CrearTestbin(simularDuplicados);
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
