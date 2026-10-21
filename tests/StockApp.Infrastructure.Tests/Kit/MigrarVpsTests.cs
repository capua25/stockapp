using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/migrar-vps.sh (spec 6.5.1 a 6.5.3 y 6.3): modos, corrida, identidad de los
/// hosts, pre-vuelo de solo lectura y confirmación. Cada test corre el script REAL en un árbol
/// temporal con la forma del repo, con HOME temporal y SIN terminal de control (setsid): nunca
/// puede quedarse leyendo el teclado de quien corre la suite.
///
/// Los dos servidores son un <c>ssh</c> falso con estado en archivos (uno por host: machine-id, estado
/// de la unit, contenedores, tamaños de disco, login 200/401/403, claves del .env del nuevo) y un
/// <c>curl</c> falso del operador (el feed del servidor nuevo). Los bloques (preparar, volcado,
/// restaurar) son ejecutables falsos del árbol que registran sus argumentos y el STOCKAPP_ENV_LOCAL
/// con el que los llamaron. Ningún test toca un servidor real ni el stockapp-pg de desarrollo.
///
/// El rastro marca cada comando remoto como LECTURA o ESCRITURA según una lista blanca de lecturas
/// del propio test (spec 5): lo que no está en la lista es una escritura, y el pre-vuelo no puede
/// tener ninguna. Todo comando al VPS viejo, que es compartido con otra aplicación, además tiene
/// que estar en la lista blanca estrecha del viejo.
/// </summary>
public class MigrarVpsTests : IDisposable
{
    private const string HostViejo = "vps-viejo.test.invalid";
    private const string HostNuevo = "vps-nuevo.test.invalid";
    private const string Contrasena = "Secreta-Admin-123-Zx9";
    private const string MachineViejo = "aaaaaaaa11111111aaaaaaaa11111111";
    private const string MachineNuevo = "bbbbbbbb22222222bbbbbbbb22222222";
    private const string Version = "1.2.3";

    // Hasta donde llega lo implementado de migrar: el pre-vuelo y la confirmación. Después de
    // eso el script termina con este código (nunca 0: un 0 sería un GO técnico que no ocurrió).
    private const int ExitFinDeLoImplementado = 1;

    private readonly string _dir;
    private readonly List<Process> _procesos = [];

    public MigrarVpsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "stockapp-migrar-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Tmp);
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(Path.Combine(Estado, "viejo"));
        Directory.CreateDirectory(Path.Combine(Estado, "nuevo"));
        ArmarArbol();
        ArmarStubs();
        EstadoInicialDeLosHosts();
        EscribirEnvs();
    }

    public void Dispose()
    {
        foreach (var p in _procesos)
        {
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            p.Dispose();
        }

        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ---------- Rutas ----------

    private string Raiz => Path.Combine(_dir, "repo");
    private string Home => Path.Combine(_dir, "home");
    private string Tmp => Path.Combine(_dir, "tmp");
    private string Bin => Path.Combine(_dir, "bin");
    private string Estado => Path.Combine(_dir, "estado");
    private string Rastro => Path.Combine(_dir, "rastro.txt");
    private string EnvViejo => Path.Combine(_dir, "env-viejo.txt");
    private string EnvNuevo => Path.Combine(_dir, "env-nuevo.txt");
    private string Script => Path.Combine(Raiz, "deploy", "migrar-vps.sh");
    private string CorridasDir => Path.Combine(Home, "stockapp-backups-vps", "corridas");
    private string LockFile => Path.Combine(Home, "stockapp-backups-vps", ".migracion.lock");

    private string Host(string lado, string archivo) => Path.Combine(Estado, lado, archivo);

    private void PonerViejo(string archivo, string contenido) => File.WriteAllText(Host("viejo", archivo), contenido);
    private void PonerNuevo(string archivo, string contenido) => File.WriteAllText(Host("nuevo", archivo), contenido);

    // ---------- Árbol, stubs y estado ----------

    private void ArmarArbol()
    {
        var repo = BashLimpio.RaizRepo();
        foreach (var rel in new[] { "deploy/migrar-vps.sh", "deploy/lib/orquestacion.sh" })
        {
            var destino = Path.Combine(Raiz, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(Path.Combine(repo, rel), destino, overwrite: true);
            File.SetUnixFileMode(destino, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Bloques falsos: registran argumentos y STOCKAPP_ENV_LOCAL, y salen con STUB_<NOMBRE>_EXIT.
        foreach (var (rel, nombre, variable) in new[]
                 {
                     ("deploy/preparar-vps-nuevo.sh", "preparar", "STUB_PREPARAR_EXIT"),
                     ("deploy/volcado-final-vps.sh", "volcado", "STUB_VOLCADO_EXIT"),
                     ("deploy/restaurar-backup.sh", "restaurar", "STUB_RESTAURAR_EXIT"),
                 })
        {
            BashLimpio.EscribirEjecutable(Path.Combine(Raiz, rel),
                "#!/usr/bin/env bash\n" +
                $"{{ printf '{nombre}\\tENV=%s\\tARGS:' \"${{STOCKAPP_ENV_LOCAL-}}\"; printf ' %s' \"$@\"; printf '\\n'; }} >> \"$RASTRO\"\n" +
                $"exit \"${{{variable}:-0}}\"\n");
        }
    }

    private const string SshFalso = """
        #!/usr/bin/env bash
        # ssh falso con un host simulado por archivos. Dos servidores: el viejo y el nuevo.
        # Reproduce de ssh lo que importa: -p y -o se aceptan, lo que sigue a user@host es UN
        # comando, el stdin de la sesión llega al comando remoto y un host inalcanzable sale 255.
        while [[ $# -gt 0 ]]; do
            case "$1" in
                -p) shift 2 ;;
                -o) shift 2 ;;
                *) break ;;
            esac
        done
        destino="$1"
        cmd="${2-}"
        host="${destino#*@}"
        case "$host" in
            vps-viejo.test.invalid) E="$ESTADO/viejo"; lado=viejo ;;
            vps-nuevo.test.invalid) E="$ESTADO/nuevo"; lado=nuevo ;;
            *)
                printf 'DESCONOCIDO\t%s\n' "$host" >> "$RASTRO"
                echo "ssh: Could not resolve hostname $host: Name or service not known" >&2
                exit 255
                ;;
        esac
        printf '%s\t%s\n' "$lado" "$cmd" >> "$RASTRO"
        if [[ -f "$E/ssh-caido" ]]; then
            echo "ssh: connect to host $host port 22: Connection refused" >&2
            exit 255
        fi
        entrada="$(cat)"
        df_linea() {
            printf 'Filesystem     1024-blocks     Used Available Capacity Mounted on\n'
            printf 'overlay           62914560 52428800 %s      84%% %s\n' "$1" "$2"
        }

        case "$cmd" in
            "cat "*"/etc/machine-id")
                if [[ -f "$E/machine-id" ]]; then cat "$E/machine-id"; else
                    echo "cat: /etc/machine-id: No such file or directory" >&2; exit 1
                fi
                ;;
            "sudo -n true")
                if [[ -f "$E/sin-sudo" ]]; then echo "sudo: a password is required" >&2; exit 1; fi
                ;;
            "systemctl is-active stockapp-api")
                estado="$(cat "$E/active")"
                echo "$estado"
                [[ "$estado" == active ]] || exit 3
                ;;
            "systemctl show -p FragmentPath stockapp-api")
                echo "FragmentPath=$(cat "$E/fragment")"
                ;;
            "test -e "*)
                ruta="${cmd#test -e }"
                if grep -qxF -- "$ruta" "$E/existen"; then exit 0; else exit 1; fi
                ;;
            "docker ps --format '{{.Names}}'")
                cat "$E/contenedores"
                ;;
            *"docker exec -i "*" psql -U "*)
                if [[ "$entrada" != *pg_database_size* ]]; then echo "stub: SQL no prevista" >&2; exit 99; fi
                if [[ -f "$E/dbsize-falla" ]]; then echo "psql: error: connection refused" >&2; exit 2; fi
                cat "$E/dbsize"
                ;;
            *"docker exec "*" df -P /var/lib/postgresql/data")
                df_linea "$(cat "$E/df-volumen-kb")" /var/lib/postgresql/data
                ;;
            "df -P "*)
                df_linea "$(cat "$E/df-fs-kb")" /home/deploy
                ;;
            "sed -n "*"/stockapp-deploy/.env"*)
                cat "$E/env-claves"
                ;;
            "curl -fsS "*"/auth/login "*)
                printf '%s' "$entrada" > "$ESTADO/login-stdin.txt"
                modo="$(cat "$E/login")"
                case "$modo" in
                    ok)
                        if ! printf '%s' "$entrada" | jq -e '(.nombreUsuario | length > 0) and (.contrasena | length > 0)' >/dev/null 2>&1; then
                            echo 'curl: (22) The requested URL returned error: 400' >&2; exit 22
                        fi
                        echo '{"token":"TOKEN-FALSO-ABC","usuario":{"id":1}}'
                        ;;
                    fallo) echo 'curl: (7) Failed to connect to 127.0.0.1 port 8443' >&2; exit 7 ;;
                    *) echo "curl: (22) The requested URL returned error: $modo" >&2; exit 22 ;;
                esac
                ;;
            "curl -fsS "*"/backups -K -")
                printf '%s' "$entrada" > "$ESTADO/backups-stdin.txt"
                if [[ "$entrada" != *"Authorization: Bearer TOKEN-FALSO-ABC"* ]]; then
                    echo 'curl: (22) The requested URL returned error: 401' >&2; exit 22
                fi
                modo="$(cat "$E/backups")"
                case "$modo" in
                    ok) echo '[{"id":9,"finalizadaEn":"2026-10-19T03:00:00Z","resultado":"Exitosa","nombreArchivo":"backup_x.dump","tamanioBytes":2048}]' ;;
                    json-malo) echo 'no soy json' ;;
                    fallo) echo 'curl: (7) Failed to connect to 127.0.0.1 port 8443' >&2; exit 7 ;;
                    *) echo "curl: (22) The requested URL returned error: $modo" >&2; exit 22 ;;
                esac
                ;;
            *)
                echo "stub ssh: comando no previsto: $cmd" >&2
                exit 99
                ;;
        esac
        """;

    private const string CurlFalso = """
        #!/usr/bin/env bash
        # curl del operador: solo el feed del servidor nuevo. Reproduce de curl -f: un 404 sale 22,
        # nadie escuchando sale 7 y un timeout sale 28.
        url="${!#}"
        printf 'operador\tcurl %s\n' "$url" >> "$RASTRO"
        case "$url" in
            */updates/releases.win.json)
                modo="$(cat "$ESTADO/feed")"
                case "$modo" in
                    ausente) echo 'curl: (22) The requested URL returned error: 404' >&2; exit 22 ;;
                    sin-escucha) echo 'curl: (7) Failed to connect' >&2; exit 7 ;;
                    timeout) echo 'curl: (28) Operation timed out' >&2; exit 28 ;;
                    ilegible) echo 'no soy json' ;;
                    *) cat "$ESTADO/feed-json" ;;
                esac
                ;;
            *)
                echo "stub curl: url no prevista: $url" >&2
                exit 99
                ;;
        esac
        """;

    private void ArmarStubs()
    {
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "ssh"), SshFalso);
        BashLimpio.EscribirEjecutable(Path.Combine(Bin, "curl"), CurlFalso);
    }

    private void EstadoInicialDeLosHosts()
    {
        PonerViejo("machine-id", MachineViejo + "\n");
        PonerViejo("active", "active\n");
        PonerViejo("fragment", "/etc/systemd/system/stockapp-api.service\n");
        PonerViejo("existen", "");
        PonerViejo("contenedores", "stockapp-pg\npinar-db\n");
        PonerViejo("dbsize", "104857600\n");
        PonerViejo("df-volumen-kb", "10485760\n");
        PonerViejo("login", "ok\n");
        PonerViejo("backups", "ok\n");

        PonerNuevo("machine-id", MachineNuevo + "\n");
        PonerNuevo("dbsize", "5242880\n");
        PonerNuevo("df-volumen-kb", "10485760\n");
        PonerNuevo("df-fs-kb", "10485760\n");
        PonerNuevo("env-claves", "POSTGRES_USER\nPOSTGRES_PASSWORD\nPOSTGRES_DB\nJWT_SECRET\n");

        File.WriteAllText(Path.Combine(Estado, "feed"), "ok\n");
        File.WriteAllText(Path.Combine(Estado, "feed-json"),
            $"{{\"Assets\":[{{\"Type\":\"Full\",\"Version\":\"{Version}\",\"FileName\":\"StockApp-{Version}-full.nupkg\",\"Size\":10,\"SHA256\":\"{new string('a', 64)}\"}}]}}\n");
    }

    private void EscribirEnvs(string hostViejo = HostViejo, string hostNuevo = HostNuevo, string saltoDeLinea = "\n")
    {
        string Env(string host, int puerto, string api, bool admin) =>
            string.Join(saltoDeLinea, new[]
            {
                $"VPS_HOST={host}", "VPS_USER=deploy", $"VPS_SSH_PORT={puerto}",
                "API_SCHEME=https", $"API_HOSTNAME={api}", "API_PORT=8443",
                "POSTGRES_USER=stockapp", "POSTGRES_DB=stockapp",
            }.Concat(admin ? [$"VPS_ADMIN_USER=martin", $"VPS_ADMIN_PASSWORD={Contrasena}"] : [])) + saltoDeLinea;

        File.WriteAllText(EnvViejo, Env(hostViejo, 2201, "viejo.test.invalid", admin: true));
        File.WriteAllText(EnvNuevo, Env(hostNuevo, 2202, "nuevo.test.invalid", admin: false));
    }

    /// <summary>Un env sin una clave (para probar que falta, sin caer a ningún default).</summary>
    private void QuitarDelEnv(string archivo, string clave) =>
        File.WriteAllLines(archivo, File.ReadAllLines(archivo).Where(l => !l.StartsWith(clave + "=")));

    // ---------- Ejecución ----------

    internal sealed record Resultado(int Exit, string Stdout, string Stderr, string Rastro)
    {
        // Después de orq_log_iniciar el stderr del script pasa por el mismo tee que el stdout: los
        // mensajes de la fase con log salen por stdout y los anteriores por stderr.
        public string Salida => Stdout + "\n" + Stderr;
        public string Todo => $"exit={Exit}\n--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}\n--- rastro ---\n{Rastro}";
    }

    private static string Sq(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>
    /// Corre el orquestador sin terminal de control. <paramref name="stdin"/> es lo que lee (la
    /// contraseña, si no está en el .env, y la palabra de confirmación del arnés).
    /// </summary>
    private Resultado Correr(string[]? args = null, string? stdin = null, IDictionary<string, string>? env = null,
        bool conArgumentosBase = true)
    {
        args ??= [];
        var todos = conArgumentosBase ? new[] { "--env-viejo", EnvViejo, "--env-nuevo", EnvNuevo }.Concat(args) : args;
        var entrada = Path.Combine(_dir, "stdin-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(entrada, stdin ?? "");

        var sb = new StringBuilder();
        sb.Append($"export PATH={Sq(Bin)}\":$PATH\"\n");
        sb.Append($"export RASTRO={Sq(Rastro)} ESTADO={Sq(Estado)} TMPDIR={Sq(Tmp)}\n");
        if (env is not null)
            foreach (var (k, v) in env) sb.Append($"export {k}={Sq(v)}\n");
        sb.Append($"cd {Sq(Raiz)}\n");
        sb.Append($"setsid -w bash {Sq(Script)} {string.Join(' ', todos.Select(Sq))} < {Sq(entrada)}\n");

        var r = BashLimpio.Ejecutar(sb.ToString(), Home);
        return new Resultado(r.ExitCode, r.Stdout, r.Stderr, File.Exists(Rastro) ? File.ReadAllText(Rastro) : "");
    }

    private Resultado CorrerCorte(string? stdin = "CONGELAR\n", IDictionary<string, string>? env = null) =>
        Correr([], stdin, env);

    // ---------- Rastro: LECTURA o ESCRITURA ----------

    private static readonly Regex[] LecturasViejo =
    [
        new(@"^sudo -n true$"),
        new(@"^cat [A-Za-z0-9._/-]*/etc/machine-id$"),
        new(@"^systemctl (is-active|is-enabled|show -p FragmentPath) stockapp-api$"),
        new(@"^test -e [A-Za-z0-9._/-]+$"),
        new(@"^docker ps --format '\{\{\.Names\}\}'$"),
        new(@"^docker exec (-i )?stockapp[a-z0-9-]* (psql -U [a-z_][a-z0-9_]* -d [a-z_][a-z0-9_]* -tA -v ON_ERROR_STOP=1|df -P /var/lib/postgresql/data)$"),
        new(@"^curl -fsS (--resolve \S+ )?(-X POST \S+/auth/login -H 'Content-Type: application/json' --data @-|\S+/backups -K -)$"),
    ];

    private static readonly Regex[] LecturasNuevoExtra =
    [
        new(@"^sudo -n docker exec (-i )?stockapp[a-z0-9-]* (psql -U [a-z_][a-z0-9_]* -d [a-z_][a-z0-9_]* -tA -v ON_ERROR_STOP=1|df -P /var/lib/postgresql/data)$"),
        new("^" + Regex.Escape("df -P \"$HOME/stockapp-deploy/migracion\"") + "$"),
        new("^" + Regex.Escape("sed -n 's/^\\([A-Za-z_][A-Za-z0-9_]*\\)=.*/\\1/p' \"$HOME/stockapp-deploy/.env\"") + "$"),
    ];

    private static IEnumerable<(string Lado, string Cmd)> ComandosRemotos(string rastro) =>
        rastro.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t', 2))
            .Where(p => p.Length == 2 && p[0] is "viejo" or "nuevo")
            .Select(p => (p[0], p[1]));

    private static bool EsLectura(string lado, string cmd) =>
        LecturasViejo.Any(r => r.IsMatch(cmd)) || (lado == "nuevo" && LecturasNuevoExtra.Any(r => r.IsMatch(cmd)));

    private static IEnumerable<string> Escrituras(string rastro) =>
        ComandosRemotos(rastro).Where(c => !EsLectura(c.Lado, c.Cmd)).Select(c => $"{c.Lado}: {c.Cmd}");

    private static IEnumerable<string> LineasDeBloque(string rastro, string bloque) =>
        rastro.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith(bloque + "\t"));

    // ---------- Corridas ----------

    private string[] Corridas() =>
        Directory.Exists(CorridasDir) ? Directory.GetDirectories(CorridasDir).OrderBy(d => d, StringComparer.Ordinal).ToArray() : [];

    private string UnaCorrida(string prefijo)
    {
        var c = Corridas().Where(d => Path.GetFileName(d).StartsWith(prefijo + "-")).ToArray();
        Assert.Single(c);
        return c[0];
    }

    private static string? ClaveDeEstado(string corrida, string clave)
    {
        var archivo = Path.Combine(corrida, "estado");
        if (!File.Exists(archivo)) return null;
        var linea = File.ReadAllLines(archivo).LastOrDefault(l => l.StartsWith(clave + "="));
        return linea?[(clave.Length + 1)..];
    }

    /// <summary>Planta una corrida anterior (ensayo, corte...) con su estado y, si se pide, los overrides.</summary>
    private string PlantarCorrida(string nombre, string estado, string? overrides = null)
    {
        var dir = Path.Combine(CorridasDir, nombre);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "estado"), estado);
        if (overrides is not null)
        {
            Directory.CreateDirectory(Path.Combine(dir, "volcado"));
            File.WriteAllText(Path.Combine(dir, "volcado", "overrides-api-env.txt"), overrides);
        }

        return dir;
    }

    private string EstadoDeEnsayo(string machineViejo = MachineViejo, string machineNuevo = MachineNuevo, string extra = "") =>
        $"modo=ensayo\ninicio_utc=2026-01-01T09:00:00Z\nmachine_id_viejo={machineViejo}\nmachine_id_nuevo={machineNuevo}\n{extra}resultado=GO\n";

    // =====================================================================================
    // Camino feliz: hasta donde llega lo implementado
    // =====================================================================================

    [Fact]
    public void CaminoFeliz_PrevueloYConfirmacion_SinEscrituras()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        Assert.Contains("pre-vuelo", r.Stdout);
        Assert.Contains("confirmación", r.Stdout);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(LineasDeBloque(r.Rastro, "volcado"));
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
    }

    [Fact]
    public void Corrida_RegistraModoIdentidadYPasos_SinSecretos()
    {
        var r = CorrerCorte();

        var corrida = UnaCorrida("corte");
        Assert.True(ClaveDeEstado(corrida, "modo") == "corte", r.Todo);
        Assert.Equal(MachineViejo, ClaveDeEstado(corrida, "machine_id_viejo"));
        Assert.Equal(MachineNuevo, ClaveDeEstado(corrida, "machine_id_nuevo"));
        Assert.Equal(EnvViejo, ClaveDeEstado(corrida, "env_viejo"));
        Assert.Equal(EnvNuevo, ClaveDeEstado(corrida, "env_nuevo"));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", ClaveDeEstado(corrida, "inicio_utc") ?? "");
        Assert.Matches(@"^ok \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z \d+$", ClaveDeEstado(corrida, "paso_1") ?? "");
        Assert.Matches(@"^ok \d{4}", ClaveDeEstado(corrida, "paso_2") ?? "");

        Assert.Equal("700", Permisos(corrida));
        Assert.Equal("600", Permisos(Path.Combine(corrida, "registro.log")));
        Assert.DoesNotContain(Contrasena, File.ReadAllText(Path.Combine(corrida, "estado")));
    }

    private static string Permisos(string ruta) =>
        Convert.ToString((int)(File.GetUnixFileMode(ruta) & (UnixFileMode)0b111_111_111), 8);

    [Fact]
    public void Ensayo_NoPideConfirmacion_YLaCorridaEsDeEnsayo()
    {
        var r = Correr(["--ensayo"], stdin: "");

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Matches(@"confirmación \.+ OMITIDO", r.Stdout);
        var corrida = UnaCorrida("ensayo");
        Assert.Equal("ensayo", ClaveDeEstado(corrida, "modo"));
        Assert.Empty(Escrituras(r.Rastro));
    }

    // =====================================================================================
    // Identidad de los hosts (A9, spec 6.3.4)
    // =====================================================================================

    [Fact]
    public void MachineIdIguales_Aborta9SinEscrituras()
    {
        PonerNuevo("machine-id", MachineViejo + "\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(Corridas());
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Fact]
    public void LosDosEnvApuntanAlMismoHost_Aborta9SinSsh()
    {
        EscribirEnvs(hostNuevo: HostViejo);

        var r = CorrerCorte();

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    [Theory]
    [InlineData("viejo")]
    [InlineData("nuevo")]
    public void MachineIdIlegible_Aborta8(string lado)
    {
        File.Delete(Host(lado, "machine-id"));

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void MachineIdQueNoEsUnMachineId_Aborta8()
    {
        PonerViejo("machine-id", "no soy un machine-id\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Theory]
    [InlineData("viejo")]
    [InlineData("nuevo")]
    public void SshCaido_Aborta8_NuncaCero(string lado)
    {
        File.WriteAllText(Host(lado, "ssh-caido"), "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_EnsayoPrevioDeOtrosServidores_Aborta3EnElCorte_PeroNoEnElEnsayo()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(machineNuevo: "cccccccc33333333cccccccc33333333"));

        var corte = CorrerCorte();
        Assert.True(corte.Exit == 3, corte.Todo);
        Assert.Contains("ensayo", corte.Salida);

        var ensayo = Correr(["--ensayo"], stdin: "");
        Assert.True(ensayo.Exit == ExitFinDeLoImplementado, ensayo.Todo);
    }

    // =====================================================================================
    // Configuración y .env
    // =====================================================================================

    [Fact]
    public void Prevuelo_EnvConCrLf_Aborta3SinSsh()
    {
        EscribirEnvs(saltoDeLinea: "\r\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Equal("", r.Rastro);
        Assert.Empty(Corridas());
    }

    [Fact]
    public void Prevuelo_EnvInexistente_Aborta3SinSsh()
    {
        File.Delete(EnvNuevo);

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    [Theory]
    [InlineData("viejo", "VPS_HOST")]
    [InlineData("viejo", "VPS_USER")]
    [InlineData("viejo", "VPS_SSH_PORT")]
    [InlineData("viejo", "VPS_ADMIN_USER")]
    [InlineData("nuevo", "VPS_HOST")]
    [InlineData("nuevo", "API_HOSTNAME")]
    public void Prevuelo_FaltaUnaClave_Aborta3SinSsh(string env, string clave)
    {
        QuitarDelEnv(env == "viejo" ? EnvViejo : EnvNuevo, clave);

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains(clave, r.Salida);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void Prevuelo_UsuarioDeVerificacion_NoCaeABootstrapAdminUser()
    {
        QuitarDelEnv(EnvViejo, "VPS_ADMIN_USER");
        File.AppendAllText(EnvViejo, "BOOTSTRAP_ADMIN_USER=admin\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("VPS_ADMIN_USER", r.Salida);
        Assert.Equal("", r.Rastro);
    }

    // =====================================================================================
    // Pre-vuelo del viejo
    // =====================================================================================

    [Fact]
    public void Prevuelo_UnitNoEsLaDeInstall_Aborta3()
    {
        PonerViejo("fragment", "/usr/lib/systemd/system/stockapp-api.service\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("FragmentPath", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Prevuelo_CongeladoExistente_Aborta3()
    {
        PonerViejo("existen", "/etc/systemd/system/stockapp-api.service.congelado\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("congelado", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
    }

    [Fact]
    public void Prevuelo_ViejoInactivo_Aborta3()
    {
        PonerViejo("active", "inactive\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("active", r.Salida);
    }

    [Fact]
    public void Prevuelo_RespuestaDeIsActiveIlegible_Aborta8()
    {
        PonerViejo("active", "quién sabe\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_SudoSinContrasena_Aborta3()
    {
        File.WriteAllText(Host("viejo", "sin-sudo"), "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("sudo", r.Salida);
    }

    [Fact]
    public void Prevuelo_ContenedorDePostgresNoCorre_Aborta3()
    {
        PonerViejo("contenedores", "pinar-db\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("stockapp-pg", r.Salida);
    }

    [Fact]
    public void Prevuelo_ListaDeContenedoresVacia_Aborta8()
    {
        PonerViejo("contenedores", "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_DiscoDelViejoInsuficienteParaElDump_Aborta3()
    {
        PonerViejo("dbsize", "104857600\n");        // 100 MiB
        PonerViejo("df-volumen-kb", "1024\n");        // 1 MiB libre

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("disco", r.Salida);
    }

    [Fact]
    public void Prevuelo_TamanoDeLaBaseIlegible_Aborta8()
    {
        PonerViejo("dbsize", "mucho\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_PsqlFalla_Aborta8_NuncaCero()
    {
        File.WriteAllText(Host("viejo", "dbsize-falla"), "");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    // =====================================================================================
    // Pre-vuelo del nuevo
    // =====================================================================================

    [Fact]
    public void Prevuelo_LlamaALaVerificacionDeLaPreparacion_ConElEnvDelNuevo()
    {
        var r = CorrerCorte();

        var llamadas = LineasDeBloque(r.Rastro, "preparar").ToList();
        Assert.True(llamadas.Count == 1, r.Todo);
        Assert.Contains($"ENV={EnvNuevo}", llamadas[0]);
        Assert.Contains($"--version {Version}", llamadas[0]);
        Assert.Contains("--verificar", llamadas[0]);
        Assert.Contains($"--env-viejo {EnvViejo}", llamadas[0]);
    }

    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 3)]
    [InlineData(8, 8)]
    [InlineData(9, 9)]
    public void Prevuelo_PreparacionIncompleta_AbortaConSuClase(int salidaDelBloque, int esperado)
    {
        var r = CorrerCorte(env: new Dictionary<string, string> { ["STUB_PREPARAR_EXIT"] = salidaDelBloque.ToString() });

        Assert.True(r.Exit == esperado, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Prevuelo_SinVerificarPreparacion_EsSoloDeArnes_YSeInformaOmitido()
    {
        var r = Correr(["--sin-verificar-preparacion"], stdin: "CONGELAR\n",
            env: new Dictionary<string, string> { ["STUB_PREPARAR_EXIT"] = "5" });

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        Assert.Contains("OMITIDO (arnés)", r.Stdout);
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
    }

    [Fact]
    public void Prevuelo_ElNuevoNoSirveElFeed_Aborta3()
    {
        File.WriteAllText(Path.Combine(Estado, "feed"), "ausente\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("feed", r.Salida);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("ilegible")]
    public void Prevuelo_FeedQueNoSePuedeConsultarOInterpretar_Aborta8(string modo)
    {
        File.WriteAllText(Path.Combine(Estado, "feed"), modo + "\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    [Fact]
    public void Prevuelo_DiscoDelNuevoInsuficiente_Aborta3()
    {
        PonerNuevo("df-fs-kb", "1024\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("disco", r.Salida);
    }

    [Fact]
    public void Prevuelo_VolumenDelNuevoInsuficiente_Aborta3()
    {
        PonerNuevo("df-volumen-kb", "1024\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("disco", r.Salida);
    }

    // =====================================================================================
    // Overrides (spec 6.5.3 punto 4)
    // =====================================================================================

    [Fact]
    public void Prevuelo_OverrideFaltanteEnElNuevo_Aborta3()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(), overrides: "Licencia__Algo\nOTRO_OVERRIDE\n");
        PonerNuevo("env-claves", "POSTGRES_USER\nOTRO_OVERRIDE\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("Licencia__Algo", r.Salida);
        Assert.DoesNotContain("OTRO_OVERRIDE", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Fact]
    public void Prevuelo_OverridesPresentesEnElNuevo_Sigue()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(), overrides: "Licencia__Algo\nOTRO_OVERRIDE\n");
        PonerNuevo("env-claves", "POSTGRES_USER\nOTRO_OVERRIDE\nLicencia__Algo\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
    }

    [Fact]
    public void Prevuelo_SinEnsayoPrevio_InformaQueNoHayLista_YSigue()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        Assert.Contains("no hay un ensayo anterior", r.Stdout);
    }

    [Fact]
    public void Prevuelo_ElListadoDeClavesDelNuevoNoPuedeLeerse_Aborta8()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(), overrides: "Licencia__Algo\n");
        File.Delete(Host("nuevo", "env-claves"));

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
    }

    // =====================================================================================
    // Credencial (spec 6.5.3 puntos 5 y 6)
    // =====================================================================================

    [Fact]
    public void Prevuelo_CredencialRechazada_Aborta3AntesDeConfirmar()
    {
        PonerViejo("login", "401\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Contains("401", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Prevuelo_SinPermisoParaBackups_Aborta3()
    {
        PonerViejo("backups", "403\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("403", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Theory]
    [InlineData("login", "429")]
    [InlineData("login", "500")]
    [InlineData("login", "fallo")]
    [InlineData("backups", "500")]
    [InlineData("backups", "fallo")]
    [InlineData("backups", "json-malo")]
    public void Prevuelo_CredencialOPermisoQueNoSePuedenConsultar_Aborta8(string que, string modo)
    {
        PonerViejo(que, modo + "\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 8, r.Todo);
        Assert.DoesNotContain("CONGELAR", r.Salida);
    }

    [Fact]
    public void Credencial_SinContrasenaEnNingunLado_EnArnesSeLeeDeStdinAntesDeLaPalabra()
    {
        QuitarDelEnv(EnvViejo, "VPS_ADMIN_PASSWORD");

        var r = CorrerCorte(stdin: $"{Contrasena}\nCONGELAR\n");

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        Assert.Contains(Contrasena, File.ReadAllText(Path.Combine(Estado, "login-stdin.txt")));
        Assert.DoesNotContain(Contrasena, r.Stdout + r.Stderr + r.Rastro);
    }

    [Fact]
    public void Credencial_SinContrasenaYSinNadaEnStdin_Aborta3()
    {
        QuitarDelEnv(EnvViejo, "VPS_ADMIN_PASSWORD");

        var r = CorrerCorte(stdin: "");

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("VPS_ADMIN_PASSWORD", r.Salida);
    }

    [Fact]
    public void Credencial_ElEntornoGanaSobreElArchivo()
    {
        const string delEntorno = "Del-Entorno-987";

        var r = CorrerCorte(env: new Dictionary<string, string> { ["VPS_ADMIN_PASSWORD"] = delEntorno });

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        var enviado = File.ReadAllText(Path.Combine(Estado, "login-stdin.txt"));
        Assert.Contains(delEntorno, enviado);
        Assert.DoesNotContain(Contrasena, enviado);
    }

    [Fact]
    public void PasswordNuncaEnArgvNiLog()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        // Control positivo: la contraseña SÍ viajó, y por el stdin del login.
        Assert.Contains(Contrasena, File.ReadAllText(Path.Combine(Estado, "login-stdin.txt")));

        var corrida = UnaCorrida("corte");
        var archivos = Directory.GetFiles(corrida, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Tmp, "*", SearchOption.AllDirectories))
            .Select(File.ReadAllText);
        var todo = r.Stdout + r.Stderr + r.Rastro + string.Join("\n", archivos);
        Assert.DoesNotContain(Contrasena, todo);
        Assert.DoesNotContain("TOKEN-FALSO-ABC", todo);
        // El token tampoco viaja en el comando: solo por el stdin de curl -K -.
        Assert.Contains("Bearer TOKEN-FALSO-ABC", File.ReadAllText(Path.Combine(Estado, "backups-stdin.txt")));
    }

    // =====================================================================================
    // Confirmación (A12, spec 6.3.6)
    // =====================================================================================

    [Fact]
    public void Confirmacion_PalabraDistinta_Sale4SinEscrituras()
    {
        var r = CorrerCorte(stdin: "congelar\n");

        Assert.True(r.Exit == 4, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(LineasDeBloque(r.Rastro, "volcado"));
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
        var corrida = UnaCorrida("corte");
        Assert.Null(ClaveDeEstado(corrida, "paso_2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CONGELAR \n")]
    [InlineData("si\n")]
    [InlineData("CONGELARX\n")]
    public void Confirmacion_CualquierOtraCosa_Sale4(string stdin)
    {
        var r = CorrerCorte(stdin: stdin);

        Assert.True(r.Exit == 4, r.Todo);
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Fact]
    public void Confirmacion_EsLoUnicoEntrePrevueloYLaPrimeraEscritura()
    {
        var r = CorrerCorte();

        var finPrevuelo = r.Stdout.IndexOf("pre-vuelo", StringComparison.Ordinal);
        var palabra = r.Stdout.IndexOf("CONGELAR", StringComparison.Ordinal);
        Assert.True(finPrevuelo >= 0 && palabra > finPrevuelo, "la confirmación tiene que venir después del pre-vuelo.\n" + r.Todo);
        // Hasta el final de lo implementado, ningún comando modificó nada y ningún bloque de corte corrió.
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(LineasDeBloque(r.Rastro, "volcado"));
        Assert.Empty(LineasDeBloque(r.Rastro, "restaurar"));
    }

    [Fact]
    public void Confirmacion_MuestraHostsVersionIndisponibilidadYComandosDeVueltaAtras_YQuedaEnElLog()
    {
        PlantarCorrida("ensayo-20260101T090000Z", EstadoDeEnsayo(
            extra: "congelado_utc=2026-01-01T10:00:00Z\napi_nueva_activa_utc=2026-01-01T10:03:12Z\n"));

        var r = CorrerCorte();

        var corrida = UnaCorrida("corte");
        var log = File.ReadAllText(Path.Combine(corrida, "registro.log"));
        foreach (var texto in new[] { r.Stdout, log })
        {
            Assert.Contains(HostViejo, texto);
            Assert.Contains(HostNuevo, texto);
            Assert.Contains(MachineViejo[..8], texto);
            Assert.Contains(MachineNuevo[..8], texto);
            Assert.DoesNotContain(MachineViejo, texto);
            Assert.Contains(Version, texto);
            Assert.Contains("3 min 12 s", texto);
            Assert.Contains("sudo systemctl unmask stockapp-api", texto);
            Assert.Contains("sudo mv /etc/systemd/system/stockapp-api.service.congelado /etc/systemd/system/stockapp-api.service", texto);
            Assert.Contains("sudo systemctl daemon-reload", texto);
            Assert.Contains("sudo systemctl start stockapp-api", texto);
            Assert.Contains("--revertir", texto);
        }
    }

    [Fact]
    public void Confirmacion_SinEnsayoPrevio_DiceQueNoHayDato()
    {
        var r = CorrerCorte();

        Assert.Contains("sin ensayo previo", r.Stdout);
    }

    [Fact]
    public void SinTerminalConHostsReales_Sale3()
    {
        EscribirEnvs(hostViejo: "viejo-real.example.org", hostNuevo: "nuevo-real.example.org");

        var r = CorrerCorte(stdin: "CONGELAR\n");

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("terminal", r.Salida);
        Assert.Equal("", r.Rastro);
        Assert.DoesNotContain("pre-vuelo", r.Salida);
    }

    [Fact]
    public void HostsReales_NoUsanElDesvioDeStdin_AunqueSoloUnoSeaInvalid()
    {
        EscribirEnvs(hostNuevo: "nuevo-real.example.org");

        var r = CorrerCorte(stdin: "CONGELAR\n");

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    public static TheoryData<string, string?> ParametrosDeArnes() => new()
    {
        { "--contenedor-viejo", "stockapp-ensayo-x" },
        { "--contenedor-nuevo", "stockapp-ensayo-y" },
        { "--raiz-remota-viejo", "/hosts/viejo" },
        { "--raiz-remota-nuevo", "/hosts/nuevo" },
        { "--sin-verificar-preparacion", null },
    };

    [Theory]
    [MemberData(nameof(ParametrosDeArnes))]
    public void ParametrosDeArnesConHostReal_Rechaza2(string parametro, string? valor)
    {
        EscribirEnvs(hostViejo: "viejo-real.example.org", hostNuevo: "nuevo-real.example.org");

        var r = Correr(valor is null ? [parametro] : [parametro, valor], stdin: "CONGELAR\n");

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Contains(parametro, r.Salida);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void InyeccionDeFallosConHostReal_Rechaza2()
    {
        EscribirEnvs(hostViejo: "viejo-real.example.org", hostNuevo: "nuevo-real.example.org");

        var r = CorrerCorte(env: new Dictionary<string, string> { ["STOCKAPP_ENSAYO_FALLAR_EN"] = "7" });

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Contains("STOCKAPP_ENSAYO_FALLAR_EN", r.Salida);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void ParametrosDeArnes_ConHostsInvalid_ReenvianLosNombresDeContenedor()
    {
        PonerViejo("contenedores", "stockapp-ensayo-origen\n");
        PonerViejo("fragment", "/hosts/viejo/etc/systemd/system/stockapp-api.service\n");

        var r = Correr(["--contenedor-viejo", "stockapp-ensayo-origen", "--contenedor-nuevo", "stockapp-ensayo-destino",
            "--raiz-remota-viejo", "/hosts/viejo", "--raiz-remota-nuevo", "/hosts/nuevo"], stdin: "CONGELAR\n");

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        var cmds = ComandosRemotos(r.Rastro).ToList();
        Assert.Contains(cmds, c => c.Lado == "viejo" && c.Cmd.Contains("docker exec -i stockapp-ensayo-origen psql"));
        Assert.Contains(cmds, c => c.Lado == "nuevo" && c.Cmd.Contains("docker exec -i stockapp-ensayo-destino psql"));
        Assert.Contains(cmds, c => c.Lado == "viejo" && c.Cmd == "cat /hosts/viejo/etc/machine-id");
        Assert.Contains(cmds, c => c.Lado == "nuevo" && c.Cmd == "cat /hosts/nuevo/etc/machine-id");
        Assert.DoesNotContain(cmds, c => c.Cmd.Contains("stockapp-pg ") && c.Cmd.Contains("docker exec"));
        Assert.Empty(Escrituras(r.Rastro));
    }

    [Theory]
    [InlineData("--contenedor-viejo", "otra-cosa")]
    [InlineData("--contenedor-nuevo", "stockapp pg")]
    [InlineData("--raiz-remota-viejo", "relativa/ruta")]
    [InlineData("--raiz-remota-nuevo", "/a/../b")]
    public void ParametrosDeArnes_ConValorFueraDePatron_Rechaza2(string parametro, string valor)
    {
        var r = Correr([parametro, valor]);

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    [Theory]
    [InlineData("--continuar-ya")]
    [InlineData("--no-existe")]
    public void ArgumentoDesconocido_Rechaza2(string arg)
    {
        var r = Correr([arg]);

        Assert.True(r.Exit == 2, r.Todo);
        Assert.Equal("", r.Rastro);
    }

    // =====================================================================================
    // Corridas sin terminar (spec 6.3.3, R24) y lock
    // =====================================================================================

    [Fact]
    public void CorridaSinTerminar_CorteNuevoSeNiega3ConLasDosSalidas()
    {
        var sinTerminar = PlantarCorrida("corte-20260101T090000Z", "modo=corte\ninicio_utc=2026-01-01T09:00:00Z\npaso_1=ok 2026-01-01T09:00:10Z 3\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains($"--continuar {sinTerminar}", r.Salida);
        Assert.Contains("--revertir", r.Salida);
        Assert.DoesNotContain("CONGELAR", r.Salida);
        Assert.Empty(Escrituras(r.Rastro));
        Assert.Empty(LineasDeBloque(r.Rastro, "preparar"));
    }

    [Fact]
    public void CorridaTerminada_NoBloquea()
    {
        PlantarCorrida("corte-20260101T090000Z", "modo=corte\ninicio_utc=2026-01-01T09:00:00Z\nresultado=FALLO-5\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
    }

    [Fact]
    public void CorridaSinEstado_NoBloquea_PorqueNoHizoNada()
    {
        // R24: quien pierde el lock deja una corrida sin estado; contarla bloquearía todos los cortes.
        Directory.CreateDirectory(Path.Combine(CorridasDir, "corte-20260101T090000Z"));

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
    }

    [Fact]
    public void CorridaDeRevertirSinTerminar_NoBloquea_PorqueRevertirEsIdempotente()
    {
        PlantarCorrida("revertir-20260101T090000Z", "modo=revertir\ninicio_utc=2026-01-01T09:00:00Z\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
    }

    [Fact]
    public async Task Lock_Tomado_Sale3()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LockFile)!);
        var titular = new Process
        {
            StartInfo = new ProcessStartInfo("flock")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        titular.StartInfo.ArgumentList.Add(LockFile);
        titular.StartInfo.ArgumentList.Add("-c");
        titular.StartInfo.ArgumentList.Add("cat");
        titular.Start();
        _procesos.Add(titular);
        try
        {
            var tomado = await EsperaMonotonica.HastaAsync(
                () => BashLimpio.Ejecutar($"exec 8>>{Sq(LockFile)}; ! flock -n 8").ExitCode == 0,
                TimeSpan.FromSeconds(10));
            Assert.True(tomado, "el titular nunca llegó a tomar el lock");

            var r = CorrerCorte();

            Assert.True(r.Exit == 3, r.Todo);
            Assert.Contains("migración", r.Salida);
            Assert.DoesNotContain("CONGELAR", r.Salida);
            // Quien pierde el lock no deja estado: no cuenta como corrida sin terminar.
            Assert.All(Corridas(), c => Assert.False(File.Exists(Path.Combine(c, "estado"))));
        }
        finally
        {
            titular.StandardInput.Close();
            if (!titular.WaitForExit(10000)) titular.Kill();
        }
    }

    // =====================================================================================
    // Cierre de la corrida y limpieza (R30)
    // =====================================================================================

    [Fact]
    public void UnFalloDelPrevuelo_CierraLaCorridaConResultadoFallo()
    {
        PonerViejo("login", "401\n");

        var r = CorrerCorte();

        Assert.True(r.Exit == 3, r.Todo);
        var corrida = UnaCorrida("corte");
        Assert.Equal("FALLO-3", ClaveDeEstado(corrida, "resultado"));
    }

    [Fact]
    public void Confirmacion_PalabraDistinta_CierraLaCorridaConFallo4()
    {
        CorrerCorte(stdin: "no\n");

        Assert.Equal("FALLO-4", ClaveDeEstado(UnaCorrida("corte"), "resultado"));
    }

    [Fact]
    public void Trap_EncadenaLaLimpiezaDeLaLib_NoQuedanTemporalesEnTmp()
    {
        // El pre-vuelo lee la identidad con ssh ANTES de que exista la corrida: ahí el stderr de ssh
        // va a un directorio privado que la lib borra en su trap EXIT. El trap propio de migrar tiene
        // que encadenar esa limpieza, no reemplazarla.
        var r = CorrerCorte(stdin: "no\n");

        Assert.True(r.Exit == 4, r.Todo);
        Assert.Empty(Directory.GetFileSystemEntries(Tmp));
    }

    // =====================================================================================
    // Alcance: el viejo es compartido (spec 5) y lo que migrar nunca hace
    // =====================================================================================

    [Fact]
    public void Prevuelo_TodoComandoAlViejoEnListaBlanca_YNingunaEscrituraAlNuevo()
    {
        var r = CorrerCorte();

        Assert.True(r.Exit == ExitFinDeLoImplementado, r.Todo);
        var cmds = ComandosRemotos(r.Rastro).ToList();
        Assert.NotEmpty(cmds.Where(c => c.Lado == "viejo"));
        Assert.NotEmpty(cmds.Where(c => c.Lado == "nuevo"));
        foreach (var (lado, cmd) in cmds.Where(c => c.Lado == "viejo"))
            Assert.True(LecturasViejo.Any(x => x.IsMatch(cmd)), $"comando al VPS viejo fuera de la lista blanca: {cmd}");
        Assert.Empty(Escrituras(r.Rastro));
        // Nada que sea global: ni el proxy de la otra app, ni systemd, ni docker fuera de stockapp.
        Assert.DoesNotContain(cmds, c => c.Lado == "viejo" && Regex.IsMatch(c.Cmd, @"\b(restart|stop|start|mask|unmask|rm|mv|kill)\b"));
    }

    [Fact]
    public void Prevuelo_NingunTmpFueraDeDockerExec()
    {
        var r = CorrerCorte();

        foreach (var (_, cmd) in ComandosRemotos(r.Rastro))
            if (!cmd.Contains("docker exec")) Assert.DoesNotContain("/tmp/", cmd);
        Assert.DoesNotContain("/tmp/", string.Join("\n", r.Rastro.Split('\n').Where(l => l.StartsWith("operador\t"))));
        Assert.Empty(Directory.GetFileSystemEntries(Tmp));
    }

    [Fact]
    public void Migrar_NuncaLlamaADeployVps()
    {
        var r = CorrerCorte();

        Assert.DoesNotContain("deploy-vps", r.Rastro);
        Assert.DoesNotContain("deploy-vps", File.ReadAllText(Script));
    }

    [Fact]
    public void ElFeedSeLeeDesdeElOperador_ConLaUrlDelNuevo_SinInsecure()
    {
        var r = CorrerCorte();

        var curls = r.Rastro.Split('\n').Where(l => l.StartsWith("operador\tcurl ")).ToList();
        Assert.Single(curls);
        Assert.Equal("operador\tcurl https://nuevo.test.invalid:8443/updates/releases.win.json", curls[0]);
        Assert.DoesNotMatch(@"curl[^\n]*\s-k\b|--insecure", File.ReadAllText(Script));
    }
}
