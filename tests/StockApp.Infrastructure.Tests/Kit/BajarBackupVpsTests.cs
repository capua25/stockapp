using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/bajar-backup-vps.sh: baja un backup YA EXISTENTE del VPS por la API
/// (login, GET /backups, GET /backups/{id}/contenido) sin escribir nada en el VPS: ni POST
/// /backups, ni archivos temporales, ni rutas del host. El contenido viaja por el stdout de ssh.
///
/// Corre el script REAL con 'ssh' y 'pg_restore' falsos por PATH ('jq' es el real). El ssh falso
/// deja dos rastros: el argv de cada invocación (lo que cualquier usuario del VPS compartido vería
/// en /proc/&lt;pid&gt;/cmdline) y, aparte, lo que llegó por stdin (donde TIENEN que viajar la
/// contraseña y el token).
/// </summary>
public class BajarBackupVpsTests : IDisposable
{
    private readonly List<string> _directorios = [];

    public void Dispose()
    {
        foreach (var d in _directorios)
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private static string RutaScriptReal([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy", "bajar-backup-vps.sh"));
    }

    private const string Contrasena = "Secreta-Admin-123";

    // ---------- Stubs ----------

    /// <summary>
    /// ssh falso: STUB_LOGIN_FALLA (curl -f ante un 401: exit 22), STUB_LISTADO (archivo con el
    /// JSON de GET /backups), STUB_LISTADO_FALLA, STUB_TAMANO_CONTENIDO (bytes que devuelve
    /// /contenido, default 2048).
    /// </summary>
    private const string SshFalso = """
        #!/usr/bin/env bash
        { printf 'SSH:'; printf ' %s' "$@"; printf '\n'; } >> "$RASTRO"
        args=("$@")
        cmd="${args[-1]}"
        registrar_stdin() { { printf 'STDIN:'; cat; printf '\n'; } >> "$RASTRO_STDIN"; }
        # Como el curl real: sin '-K -' no lee el header de stdin y la API responde 401.
        exigir_header_por_stdin() {
            if [[ "$cmd" != *" -K -" ]]; then
                echo 'curl: (22) The requested URL returned error: 401' >&2
                exit 22
            fi
        }

        case "$cmd" in
            *"/auth/login"*)
                registrar_stdin
                if [[ -n "${STUB_LOGIN_FALLA:-}" ]]; then
                    echo 'curl: (22) The requested URL returned error: 401' >&2
                    exit 22
                fi
                echo '{"token":"FAKE-TOKEN-123","usuario":{"id":1}}'
                ;;
            *"/contenido"*)
                registrar_stdin
                exigir_header_por_stdin
                head -c "${STUB_TAMANO_CONTENIDO:-2048}" /dev/zero | tr '\0' 'B'
                ;;
            *"/backups"*)
                registrar_stdin
                exigir_header_por_stdin
                if [[ -n "${STUB_LISTADO_FALLA:-}" ]]; then
                    echo 'curl: (7) Failed to connect (simulado)' >&2
                    exit 7
                fi
                cat "$STUB_LISTADO"
                ;;
            *)
                echo "stub ssh: comando no previsto: $cmd" >&2
                exit 99
                ;;
        esac
        """;

    private const string PgRestoreFalso = """
        #!/usr/bin/env bash
        { printf 'PG_RESTORE:'; printf ' %s' "$@"; printf '\n'; } >> "$RASTRO"
        if [[ -n "${STUB_PG_RESTORE_FALLA:-}" ]]; then
            echo 'pg_restore: error: input file does not appear to be a valid archive (simulado)' >&2
            exit 1
        fi
        [[ "$1" == "--list" && -f "$2" ]] || { echo "stub pg_restore: uso inesperado: $*" >&2; exit 99; }
        echo ';     Archive created at 2026-10-19 03:00:00 UTC'
        """;

    private const string ListadoPorDefecto = """
        [
          {"id":10,"finalizadaEn":"2026-10-20T03:00:00Z","resultado":"Fallida","nombreArchivo":null,"tamanioBytes":null,"motivoFallo":"simulado"},
          {"id":9,"finalizadaEn":"2026-10-19T03:00:00.5Z","resultado":"Exitosa","nombreArchivo":"backup_20261019_030000500.dump","tamanioBytes":2048,"motivoFallo":null},
          {"id":7,"finalizadaEn":"2026-10-18T03:00:00.123Z","resultado":"Exitosa","nombreArchivo":"backup_20261018_030000123.dump","tamanioBytes":2048,"motivoFallo":null}
        ]
        """;

    private sealed record Resultado(int ExitCode, string Stdout, string Stderr, string Rastro, string RastroStdin, string Salida)
    {
        public string Diag => $"exit={ExitCode}\nstdout={Stdout}\nstderr={Stderr}\nrastro=\n{Rastro}";
    }

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta, (UnixFileMode)0b111_101_101);
    }

    private Resultado Correr(
        string args = "", IDictionary<string, string>? env = null, string listado = ListadoPorDefecto,
        string envOperadorExtra = "", string? envLocal = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bajar-backup-test-" + Guid.NewGuid().ToString("N")[..12]);
        _directorios.Add(dir);
        var testbin = Path.Combine(dir, "testbin");
        Directory.CreateDirectory(testbin);
        EscribirEjecutable(Path.Combine(testbin, "ssh"), SshFalso);
        EscribirEjecutable(Path.Combine(testbin, "pg_restore"), PgRestoreFalso);
        File.WriteAllText(Path.Combine(dir, "listado.json"), listado);

        var archivoEnv = Path.Combine(dir, "operador.env");
        File.WriteAllText(archivoEnv,
            "POSTGRES_USER=stockapp\nPOSTGRES_DB=stockapp\nAPI_PORT=5080\n" +
            "BOOTSTRAP_ADMIN_USER=admin\nBOOTSTRAP_PASSWORD=Bootstrap-Viejo-1\n" + envOperadorExtra);
        var rastro = Path.Combine(dir, "rastro");
        var rastroStdin = Path.Combine(dir, "rastro-stdin");
        var salida = Path.Combine(dir, "salida");

        var variables = new Dictionary<string, string>
        {
            ["VPS_HOST"] = "vps-viejo.test.invalid", ["VPS_USER"] = "operador", ["VPS_SSH_PORT"] = "2222",
            ["VPS_ADMIN_PASSWORD"] = Contrasena,
        };
        foreach (var kv in env ?? new Dictionary<string, string>()) variables[kv.Key] = kv.Value;

        var script =
            "exec </dev/null\n" +
            // Higiene (ruling F2): nada del entorno de quien corre la suite se cuela en la corrida.
            "unset STOCKAPP_ENV_LOCAL\n" +
            "for v in $(compgen -v VPS_); do unset \"$v\"; done\n" +
            $"export PATH=\"{testbin}:$PATH\"\n" +
            $"export RASTRO=\"{rastro}\" RASTRO_STDIN=\"{rastroStdin}\" STUB_LISTADO=\"{dir}/listado.json\"\n" +
            $"export STOCKAPP_ENV_LOCAL=\"{envLocal ?? archivoEnv}\"\n" +
            string.Concat(variables.Select(kv => $"export {kv.Key}=\"{kv.Value}\"\n")) +
            $"bash \"{RutaScriptReal()}\" --salida \"{salida}\" {args}";
        var (exit, stdout, stderr) = EjecutorBash.Ejecutar(script);
        return new Resultado(exit, stdout, stderr,
            File.Exists(rastro) ? File.ReadAllText(rastro) : "",
            File.Exists(rastroStdin) ? File.ReadAllText(rastroStdin) : "",
            salida);
    }

    private static Dictionary<string, string> Env(params (string Clave, string Valor)[] pares) =>
        pares.ToDictionary(p => p.Clave, p => p.Valor);

    private static string[] Dumps(Resultado r) =>
        Directory.Exists(r.Salida) ? Directory.GetFiles(r.Salida, "*.dump") : [];

    private static string Sha256(string ruta) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta))).ToLowerInvariant();

    // ---------- Camino feliz ----------

    [Fact]
    public void CaminoFeliz_BajaLaUltimaExitosaPorStdout()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        var dump = Assert.Single(Dumps(r));
        Assert.Equal("backup_20261019_030000500.dump", Path.GetFileName(dump));
        Assert.Equal(2048, new FileInfo(dump).Length);
        Assert.Equal((UnixFileMode)0b111_000_000, File.GetUnixFileMode(r.Salida));
        Assert.Equal((UnixFileMode)0b110_000_000, File.GetUnixFileMode(dump));

        var archivoSha = dump + ".sha256";
        Assert.Equal($"{Sha256(dump)}  backup_20261019_030000500.dump", File.ReadAllText(archivoSha).Trim());
        Assert.Equal((UnixFileMode)0b110_000_000, File.GetUnixFileMode(archivoSha));

        Assert.Contains("/backups/9/contenido", r.Rastro);
        Assert.Contains("PG_RESTORE: --list", r.Rastro);
        // La tabla de corridas se imprime siempre, con las tres.
        foreach (var nombre in new[] { "backup_20261018_030000123.dump", "backup_20261019_030000500.dump", "Fallida" })
            Assert.Contains(nombre, r.Stdout);
    }

    [Fact]
    public void ConId_BajaEsaCorrida()
    {
        var r = Correr("--id 7");

        Assert.True(r.ExitCode == 0, r.Diag);
        Assert.Equal("backup_20261018_030000123.dump", Path.GetFileName(Assert.Single(Dumps(r))));
        Assert.Contains("/backups/7/contenido", r.Rastro);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("99")]
    public void IdFallidaOInexistente_Aborta3(string id)
    {
        var r = Correr($"--id {id}");

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.DoesNotContain("/contenido", r.Rastro);
        Assert.Empty(Dumps(r));
    }

    [Fact]
    public void SinCorridasExitosas_Aborta3()
    {
        var r = Correr(listado:
            """[{"id":10,"finalizadaEn":"2026-10-20T03:00:00Z","resultado":"Fallida","nombreArchivo":null,"tamanioBytes":null,"motivoFallo":"x"}]""");

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.DoesNotContain("/contenido", r.Rastro);
    }

    // ---------- Solo lectura en el VPS, secretos fuera del argv ----------

    [Fact]
    public void NuncaHacePostNiMktempNiEscribeEnElVps()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        var lineasSsh = r.Rastro.Split('\n').Where(l => l.StartsWith("SSH:")).ToList();
        Assert.NotEmpty(lineasSsh);
        foreach (var l in lineasSsh)
        {
            Assert.DoesNotMatch(@"-X POST \S*/backups", l);
            Assert.DoesNotContain("mktemp", l);
            Assert.DoesNotContain(" -o '/", l);
            Assert.DoesNotContain("/tmp/", l);
        }
        // El único POST es el login.
        Assert.All(lineasSsh.Where(l => l.Contains("-X POST")), l => Assert.Contains("/auth/login", l));
    }

    [Fact]
    public void SecretosNuncaEnArgv()
    {
        var r = Correr();

        // Primero el argv: si el secreto viajó por ahí, eso es lo que tiene que decir el rojo.
        Assert.DoesNotContain(Contrasena, r.Rastro);
        Assert.DoesNotContain("FAKE-TOKEN", r.Rastro);
        Assert.DoesNotContain(Contrasena, r.Stdout + r.Stderr);
        Assert.True(r.ExitCode == 0, r.Diag);
        // ...y viajaron por stdin, que es donde tienen que ir (si no, el test de arriba no prueba nada).
        Assert.Contains(Contrasena, r.RastroStdin);
        Assert.Contains("Authorization: Bearer FAKE-TOKEN-123", r.RastroStdin);
    }

    // ---------- Fallos ----------

    [Fact]
    public void ListadoFalla_Aborta8()
    {
        var r = Correr(env: Env(("STUB_LISTADO_FALLA", "1")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.DoesNotContain("/contenido", r.Rastro);
    }

    [Fact]
    public void LoginFalla_Aborta3()
    {
        var r = Correr(env: Env(("STUB_LOGIN_FALLA", "1")));

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.DoesNotContain("/backups", r.Rastro);
    }

    [Fact]
    public void TamanoDistintoDelListado_Aborta4YBorra()
    {
        var r = Correr(env: Env(("STUB_TAMANO_CONTENIDO", "2000")));

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.Empty(Dumps(r));
    }

    [Fact]
    public void PgRestoreListFalla_Aborta4YBorra()
    {
        var r = Correr(env: Env(("STUB_PG_RESTORE_FALLA", "1")));

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.Empty(Dumps(r));
    }

    [Fact]
    public void NombreArchivoInseguro_Aborta4()
    {
        var r = Correr(listado:
            """[{"id":9,"finalizadaEn":"2026-10-19T03:00:00Z","resultado":"Exitosa","nombreArchivo":"../x.dump","tamanioBytes":2048,"motivoFallo":null}]""");

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.DoesNotContain("/contenido", r.Rastro);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(r.Salida)!, "x.dump")));
    }

    // ---------- Configuración ----------

    [Fact]
    public void ApiSchemeHttps_UsaResolveSinInsecure()
    {
        var r = Correr(envOperadorExtra: "API_SCHEME=https\nAPI_HOSTNAME=api.test.invalid\n");

        Assert.True(r.ExitCode == 0, r.Diag);
        Assert.Contains("https://api.test.invalid:5080/auth/login", r.Rastro);
        Assert.Contains("https://api.test.invalid:5080/backups", r.Rastro);
        Assert.Contains("--resolve api.test.invalid:5080:127.0.0.1", r.Rastro);
        Assert.DoesNotContain("--insecure", r.Rastro);
        Assert.DoesNotMatch(@"(?m)(?:^|\s)-k(?:\s|$)", r.Rastro);
        Assert.DoesNotContain("http://127.0.0.1", r.Rastro);
    }

    [Fact]
    public void StockappEnvLocalInexistente_AbortaSinTocarSsh()
    {
        var inexistente = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N") + ".env");

        var r = Correr(envLocal: inexistente);

        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("STOCKAPP_ENV_LOCAL", r.Stderr);
        Assert.Contains(inexistente, r.Stderr);
        Assert.Equal("", r.Rastro);
    }
}
