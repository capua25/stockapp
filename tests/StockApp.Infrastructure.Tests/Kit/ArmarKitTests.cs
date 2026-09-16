using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/armar-kit.sh (Task 4.1, plan 2026-09-12, líneas ~2478-2657).
///
/// A diferencia de 01-bootstrap.sh/02-instalar.sh/04-licencia.sh, armar-kit.sh NO necesita root
/// ni toca /etc ni /opt: solo lee deploy/dist, releases/win y deploy/kit, y escribe en
/// deploy/kit-dist. Por eso estos tests NO corren dentro de un contenedor: arman un REPO FALSO
/// completo (deploy/kit, deploy/dist, releases/win, deploy/*) bajo un directorio temporal, le
/// copian ahí una copia de deploy/armar-kit.sh (REPO_ROOT se resuelve del propio
/// BASH_SOURCE del script, así que corre "de verdad" con ese repo falso como raíz) y lo corren
/// directo con 'docker' stubeado por PATH. 'tar' se deja real: es inofensivo y rápido.
///
/// El stub de 'docker' registra cada invocación completa (para poder verificar, sin ejecutar
/// nada pesado, que la verificación offline usa --network none) y, cuando ve un '-o &lt;ruta&gt;'
/// (el patrón de 'docker save ... -o archivo'), toca ese archivo -- si no, el chmod 0644 de la
/// Desviación #2 fallaría contra un archivo que el stub nunca creó, tumbando el camino feliz por
/// una razón ajena a lo que el test quiere probar. Nunca ejecuta 'docker pull' ni 'docker run'
/// de verdad: eso es intencional (ver el brief de la Task 4.1) y es lo que se prueba en el Paso
/// 6, con el build real.
///
/// Lo que NO se prueba acá: que los .deb bajados adentro del contenedor sean los correctos, ni
/// que dpkg -i realmente deje los binarios -- eso requiere Internet y contenedores reales, y es
/// exactamente el trabajo del Paso 6 (build real) de esta tarea, no de la suite de xUnit.
/// </summary>
public class ArmarKitTests
{
    private static string RepoReal([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    private static string RutaScriptReal([CallerFilePath] string archivo = "")
        => Path.Combine(RepoReal(archivo), "deploy", "armar-kit.sh");

    // ---------- Stub de docker ----------

    /// <summary>
    /// Registra cada invocación en $RASTRO_DOCKER (con %q, para poder buscar flags exactos como
    /// "--network none" sin ambigüedad de espacios) y, si ve "-o &lt;ruta&gt;", toca esa ruta --
    /// así 'docker save ... -o x.tar' dentro del script real deja un archivo que el chmod
    /// posterior (Desviación #2) puede tocar sin fallar. También, por cada "-v ORIGEN:destino"
    /// SIN ":ro" al final, deja un .deb dummy en ORIGEN -- así la autoguardia de armar-kit.sh
    /// (que confirma contra el filesystem del host que la descarga de .deb realmente dejó algo,
    /// agregada tras un fallo real del Paso 6 donde 'docker run' volvía 0 con offline/docker/
    /// vacío) no tumba estos tests: el stub no baja nada de verdad, pero tiene que simular que
    /// "algo quedó" en los mounts de escritura. Cualquier otra invocación (pull) sale 0 sin
    /// hacer nada más: no se necesita más para custodiar las reglas determinísticas.
    /// </summary>
    private const string DockerStub = """
        #!/usr/bin/env bash
        {
            printf 'ARGS:'
            printf ' %q' "$@"
            printf '\n'
        } >> "${RASTRO_DOCKER:-/dev/null}"
        args=("$@")
        for ((i = 0; i < ${#args[@]}; i++)); do
            if [[ "${args[i]}" == "-o" && $((i + 1)) -lt ${#args[@]} ]]; then
                touch "${args[i + 1]}" 2>/dev/null || true
            fi
            if [[ "${args[i]}" == "-v" && $((i + 1)) -lt ${#args[@]} ]]; then
                montaje="${args[i + 1]}"
                if [[ "$montaje" != *:ro ]]; then
                    origen="${montaje%%:*}"
                    touch "${origen}/paquete-dummy.deb" 2>/dev/null || true
                fi
            fi
        done
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

    private static string CrearTestbin()
    {
        var dir = Path.Combine(Path.GetTempPath(), "armar-kit-testbin-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        EscribirEjecutable(Path.Combine(dir, "docker"), DockerStub);
        return dir;
    }

    // ---------- Repo falso ----------

    /// <summary>
    /// Arma un repo falso completo bajo un directorio temporal: deploy/kit (con VERSION, un
    /// script 0*.sh, lib/*.sh, LEEME.md y clientes/LEEME-clientes.md dummy -- la ausencia REAL de
    /// esos dos en este repo, hoy, es harina de la Task 4.2 y se ejerce solo en el build real del
    /// Paso 6, no acá), deploy/{install.sh,wait-for-postgres.sh,stockapp-api.service,
    /// docker-compose.postgres.yml,.env.example,DEPLOY.md} dummy, deploy/dist con los tarballs
    /// pedidos y releases/win con los Setup.exe pedidos.
    ///
    /// Cuando <paramref name="mutarGuardiaEnv"/> es true, clona el TEXTO ACTUAL del script real
    /// (leído de disco en cada llamada, no embebido en C#) y le cambia la única línea que instala
    /// ".env.example" en servidor/ para que instale ".env" en su lugar -- simulando el bug que la
    /// guardia de la Task 4.1 existe para atajar (que el .env REAL, con secretos, se cuele en el
    /// pendrive). El script real, tal como se lo pide el plan, NUNCA copia .env por construcción
    /// (usa nombres de archivo exactos, no un glob) -- así que ese camino no es alcanzable sin
    /// mutar. Esto es intencional: es una guardia de defensa en profundidad, y la forma honesta de
    /// probarla es mutar el punto exacto que la haría necesaria y confirmar que SIGUE frenando.
    /// También se agrega un deploy/.env real con contenido "secreto" en el repo falso, para que la
    /// mutación tenga algo real que copiar.
    /// </summary>
    private static string CrearRepoFixture(
        string[] tarballs,
        string[] setups,
        bool mutarGuardiaEnv = false,
        bool incluirLeeme = true,
        bool incluirLeemeClientes = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "armar-kit-fixture-" + Guid.NewGuid());
        var deploy = Path.Combine(root, "deploy");
        var kit = Path.Combine(deploy, "kit");
        var dist = Path.Combine(deploy, "dist");
        var releasesWin = Path.Combine(root, "releases", "win");

        Directory.CreateDirectory(Path.Combine(kit, "lib"));
        Directory.CreateDirectory(Path.Combine(kit, "clientes"));
        Directory.CreateDirectory(dist);
        Directory.CreateDirectory(releasesWin);

        File.WriteAllText(Path.Combine(kit, "VERSION"), "24.04\n");
        EscribirEjecutable(Path.Combine(kit, "00-preflight.sh"), "#!/usr/bin/env bash\nexit 0\n");
        EscribirEjecutable(Path.Combine(kit, "lib", "log.sh"), "#!/usr/bin/env bash\n");
        if (incluirLeeme)
            File.WriteAllText(Path.Combine(kit, "LEEME.md"), "# dummy\n");
        if (incluirLeemeClientes)
            File.WriteAllText(Path.Combine(kit, "clientes", "LEEME-clientes.md"), "# dummy\n");

        EscribirEjecutable(Path.Combine(deploy, "install.sh"), "#!/usr/bin/env bash\nexit 0\n");
        EscribirEjecutable(Path.Combine(deploy, "wait-for-postgres.sh"), "#!/usr/bin/env bash\nexit 0\n");
        File.WriteAllText(Path.Combine(deploy, "stockapp-api.service"), "[Unit]\nDescription=dummy\n");
        File.WriteAllText(Path.Combine(deploy, "docker-compose.postgres.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(deploy, ".env.example"), "API_PORT=5299\n");
        File.WriteAllText(Path.Combine(deploy, "DEPLOY.md"), "# dummy\n");

        foreach (var t in tarballs)
            File.WriteAllText(Path.Combine(dist, t), "contenido-dummy-tarball");
        foreach (var s in setups)
            File.WriteAllText(Path.Combine(releasesWin, s), "contenido-dummy-setup");

        var scriptReal = File.ReadAllText(RutaScriptReal());
        if (mutarGuardiaEnv)
        {
            const string marca = "\"${REPO_ROOT}/deploy/.env.example\"";
            Assert.True(scriptReal.Contains(marca),
                $"El script ya no instala servidor/ con la línea exacta esperada ('{marca}'). " +
                "Esta mutación de test necesita revisarse contra la nueva forma del script.");
            scriptReal = scriptReal.Replace(marca, "\"${REPO_ROOT}/deploy/.env\"");
            File.WriteAllText(Path.Combine(deploy, ".env"), "SECRETO_REAL=no-debe-viajar-nunca\n");
        }
        EscribirEjecutable(Path.Combine(deploy, "armar-kit.sh"), scriptReal);

        return root;
    }

    /// <summary>Corre deploy/armar-kit.sh del repo falso con 'docker' stubeado por PATH.</summary>
    private static (int ExitCode, string Stdout, string Stderr, string RastroDocker) Correr(
        string repoFixture, string argumentos)
    {
        var testbin = CrearTestbin();
        var rastroDocker = Path.Combine(Path.GetTempPath(), "armar-kit-rastro-docker-" + Guid.NewGuid());
        try
        {
            var script =
                $"export PATH=\"{testbin}:$PATH\"\n" +
                $"export RASTRO_DOCKER=\"{rastroDocker}\"\n" +
                $"bash \"{repoFixture}/deploy/armar-kit.sh\" {argumentos}";
            var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(script);
            var rastro = File.Exists(rastroDocker) ? File.ReadAllText(rastroDocker) : "";
            return (exitCode, stdout, stderr, rastro);
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
            if (File.Exists(rastroDocker)) File.Delete(rastroDocker);
        }
    }

    // ---------- Guardián: sin argumento de versión ----------

    [Fact]
    public void SinArgumentoDeVersion_MuestraUsoYAborta()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "");

            Assert.NotEqual(0, exitCode);
            Assert.Contains("Uso:", stdout + stderr);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Guardián de la Desviación #1: tarball ambiguo ----------

    [Fact]
    public void VariosTarballsCandidatos_AbortaListandolosATodos()
    {
        var repo = CrearRepoFixture(
            tarballs:
            [
                "stockapp-api-1.0.0-linux-x64.tar.gz",
                "stockapp-api-1.4.0-linux-x64.tar.gz",
                "stockapp-api-20260731121658-linux-x64.tar.gz",
            ],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "1.0.0");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("stockapp-api-1.0.0-linux-x64.tar.gz", salida);
            Assert.Contains("stockapp-api-1.4.0-linux-x64.tar.gz", salida);
            Assert.Contains("stockapp-api-20260731121658-linux-x64.tar.gz", salida);
            Assert.Contains("--tarball", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void UnSoloTarball_LoUsaYTerminaOk()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "1.0.0");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            Assert.Contains("[armar-kit] OK.", stdout);

            var destino = Path.Combine(repo, "deploy", "kit-dist", "stockapp-kit-1.0.0");
            Assert.True(File.Exists(Path.Combine(destino, "offline", "stockapp-api-1.0.0-linux-x64.tar.gz")));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void FlagTarballExplicito_UsaEseAunqueHayaVarios()
    {
        var repo = CrearRepoFixture(
            tarballs:
            [
                "stockapp-api-1.0.0-linux-x64.tar.gz",
                "stockapp-api-1.4.0-linux-x64.tar.gz",
            ],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var elegido = Path.Combine(repo, "deploy", "dist", "stockapp-api-1.4.0-linux-x64.tar.gz");
            var (exitCode, stdout, stderr, _) = Correr(repo, $"1.0.0 --tarball \"{elegido}\"");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            var destino = Path.Combine(repo, "deploy", "kit-dist", "stockapp-kit-1.0.0");
            Assert.True(File.Exists(Path.Combine(destino, "offline", "stockapp-api-1.4.0-linux-x64.tar.gz")));
            Assert.False(File.Exists(Path.Combine(destino, "offline", "stockapp-api-1.0.0-linux-x64.tar.gz")));
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void FlagTarballConRutaInexistente_AbortaConMensajeClaro()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var inexistente = Path.Combine(repo, "deploy", "dist", "no-existe-esto.tar.gz");
            var (exitCode, stdout, stderr, _) = Correr(repo, $"1.0.0 --tarball \"{inexistente}\"");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("--tarball", salida);
            Assert.Contains("no existe", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void CeroTarballs_AbortaMencionandoPublishApi()
    {
        var repo = CrearRepoFixture(
            tarballs: [],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "1.0.0");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("publish-api.sh", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Guardián del *Setup.exe ----------

    [Fact]
    public void CeroSetups_Aborta()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: []);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "1.0.0");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("no hay ningún", salida);
            Assert.Contains("Setup.exe", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    [Fact]
    public void VariosSetups_AbortaConMensajeDistinguibleDelDeCero()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: ["GestionMunicipal-win-Setup.exe", "OtroInstalador-Setup.exe"]);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "1.0.0");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains("no sé cuál", salida);
            // El mensaje del caso "cero" no debe aparecer acá: son guardias distintas.
            Assert.DoesNotContain("no hay ningún", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Guardián del .env ----------

    /// <summary>
    /// Ver la explicación completa en CrearRepoFixture (mutarGuardiaEnv): esta guardia es de
    /// defensa en profundidad y, tal como está escrito el script (nombres de archivo exactos, sin
    /// glob), no es alcanzable sin mutar el punto exacto que la volvería necesaria. Se mutan acá
    /// el TEXTO del script real leído de disco -- así que si en el futuro alguien saca la guardia
    /// del script real (deploy/armar-kit.sh), este test se pone en rojo, tal como debe.
    /// </summary>
    [Fact]
    public void GuardiaEnv_SiElOrigenTerminaCopiandoUnEnvReal_Aborta()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: ["GestionMunicipal-win-Setup.exe"],
            mutarGuardiaEnv: true);
        try
        {
            var (exitCode, stdout, stderr, _) = Correr(repo, "1.0.0");
            var salida = stdout + stderr;

            Assert.NotEqual(0, exitCode);
            Assert.Contains(".env real", salida);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }

    // ---------- Guardián de la Desviación #2 y de la verificación sin red ----------

    [Fact]
    public void VerificacionDelPayloadOffline_CorreConNetworkNone()
    {
        var repo = CrearRepoFixture(
            tarballs: ["stockapp-api-1.0.0-linux-x64.tar.gz"],
            setups: ["GestionMunicipal-win-Setup.exe"]);
        try
        {
            var (exitCode, stdout, stderr, rastro) = Correr(repo, "1.0.0");

            Assert.True(exitCode == 0, $"stdout={stdout}\nstderr={stderr}");
            // Si alguien saca --network none del docker run de verificación, esta línea deja de
            // aparecer en el rastro y el test se pone en rojo -- ESE es el guardián.
            Assert.Contains("run --rm --network none", rastro);
        }
        finally { Directory.Delete(repo, recursive: true); }
    }
}
