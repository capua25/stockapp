using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/volcado-final-vps.sh: en el corte, congela la API del VPS viejo (stop +
/// mask), verifica que nadie esté conectado a la base, vuelca DENTRO del contenedor, arma el
/// manifiesto y trae todo verificado por el stdout de ssh.
///
/// Corre el script REAL del repo con 'ssh' y 'scp' falsos por PATH (patrón DeployVpsTests.SshFalso:
/// rastro de cada invocación + respuesta según el CONTENIDO del comando remoto). El estado de la
/// unit stockapp-api vive en archivos del test: 'stop' la deja inactive, 'mask' la deja masked y
/// 'start' la vuelve a active (salvo que esté enmascarada, como el systemctl real). El rastro se
/// escribe sin escapar (%s) para poder afirmar sobre el comando remoto tal como viaja.
/// </summary>
public class VolcadoFinalVpsTests : IDisposable
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
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy", "volcado-final-vps.sh"));
    }

    // ---------- Stubs ----------

    /// <summary>
    /// ssh falso. Variables que lo gobiernan (todas opcionales):
    /// STUB_ACTIVA_TRAS_STOP, STUB_CONEXIONES (default 0; 'x' = salida basura),
    /// STUB_FALLAR_CONEXIONES, STUB_SHA_REMOTO (default: sha real del contenido que devuelve 'cat'),
    /// STUB_TAMANO_DUMP (default 2048; manda el tamaño que reporta 'stat' Y los bytes de 'cat'),
    /// STUB_DOCKER_PS_FINAL_DISTINTO, STUB_SUDO_FALLA, STUB_SUDO_ADVIERTE (todo 'sudo …' escribe una
    /// advertencia en stderr, como un host que no resuelve su propio nombre), STUB_WORKERS (procesos
    /// que no son clientes conectados a la base: autovacuum y similares), STUB_FRAGMENT (FragmentPath
    /// que reporta systemd), STUB_CONGELADO_EXISTE, STUB_TAR_CORRUPTO.
    ///
    /// Fiel a systemd real en lo que importa para el congelamiento (verificado con systemd 255):
    /// install.sh deja la unit como ARCHIVO REGULAR en /etc/systemd/system, y 'systemctl mask' sobre
    /// eso falla con rc 1 y "already exists"; mask sin -q escribe "Created symlink" en STDERR aunque
    /// funcione. El layout vive en $STUB_UNIT: 'regular' (en /etc) o 'movida' (.congelado).
    /// </summary>
    private const string SshFalso = """
        #!/usr/bin/env bash
        {
            printf 'SSH:'
            printf ' %s' "$@"
            printf '\n'
        } >> "$RASTRO"

        args=("$@")
        cmd="${args[-1]}"
        contenido() { head -c "${STUB_TAMANO_DUMP:-2048}" /dev/zero | tr '\0' 'A'; }
        unit_fragmento="/etc/systemd/system/stockapp-api.service"

        if [[ -n "${STUB_SUDO_ADVIERTE:-}" && "$cmd" == sudo* ]]; then
            echo 'sudo: unable to resolve host vps-viejo: Name or service not known' >&2
        fi

        case "$cmd" in
            "sudo -n true")
                if [[ -n "${STUB_SUDO_FALLA:-}" ]]; then echo 'sudo: a password is required' >&2; exit 1; fi
                exit 0
                ;;
            *"systemctl is-active stockapp-api"*)
                estado="$(cat "$STUB_ESTADO")"
                echo "$estado"
                [[ "$estado" == active ]] && exit 0
                exit 3
                ;;
            *"systemctl is-enabled stockapp-api"*)
                mascara="$(cat "$STUB_MASCARA")"
                echo "$mascara"
                [[ "$mascara" == enabled ]] && exit 0
                exit 1
                ;;
            *"systemctl stop stockapp-api"*)
                [[ -n "${STUB_ACTIVA_TRAS_STOP:-}" ]] || echo inactive > "$STUB_ESTADO"
                exit 0
                ;;
            *"systemctl show -p FragmentPath stockapp-api"*)
                if [[ "$(cat "$STUB_UNIT")" == regular ]]; then
                    echo "FragmentPath=${STUB_FRAGMENT:-$unit_fragmento}"
                else
                    echo "FragmentPath="
                fi
                exit 0
                ;;
            *"systemctl daemon-reload"*)
                exit 0
                ;;
            *"systemctl"*"mask stockapp-api"*)
                if [[ "$(cat "$STUB_UNIT")" == regular ]]; then
                    echo "Failed to mask unit, file \"$unit_fragmento\" already exists." >&2
                    exit 1
                fi
                echo masked > "$STUB_MASCARA"
                [[ "$cmd" == *" -q "* ]] || echo "Created symlink $unit_fragmento → /dev/null." >&2
                [[ -z "${STUB_MASK_ADVIERTE:-}" ]] || echo 'systemctl: advertencia simulada tras enmascarar' >&2
                exit 0
                ;;
            *"test -e $unit_fragmento.congelado"*)
                [[ -n "${STUB_CONGELADO_EXISTE:-}" || "$(cat "$STUB_UNIT")" == movida ]] && exit 0
                exit 1
                ;;
            *"mv $unit_fragmento $unit_fragmento.congelado"*)
                if [[ "$(cat "$STUB_UNIT")" != regular ]]; then
                    echo "mv: cannot stat '$unit_fragmento': No such file or directory" >&2
                    exit 1
                fi
                echo movida > "$STUB_UNIT"
                [[ -z "${STUB_MV_ADVIERTE:-}" ]] || echo 'mv: advertencia simulada tras mover' >&2
                exit 0
                ;;
            *"systemctl start stockapp-api"*)
                if [[ "$(cat "$STUB_MASCARA")" == masked ]]; then
                    echo 'Failed to start stockapp-api.service: Unit stockapp-api.service is masked.' >&2
                    exit 1
                fi
                echo active > "$STUB_ESTADO"
                exit 0
                ;;
            *"docker inspect"*)
                echo true
                exit 0
                ;;
            *"docker ps --format"*)
                n=$(( $(cat "$STUB_PS_CONTADOR") + 1 ))
                echo "$n" > "$STUB_PS_CONTADOR"
                printf 'stockapp-pg\npinar-web\n'
                if [[ "$n" -gt 1 && -n "${STUB_DOCKER_PS_FINAL_DISTINTO:-}" ]]; then echo 'otro-contenedor'; fi
                exit 0
                ;;
            *"psql"*)
                entrada="$(cat)"
                case "$entrada" in
                    *pg_stat_activity*)
                        if [[ -n "${STUB_FALLAR_CONEXIONES:-}" ]]; then
                            echo 'psql: error: connection to server failed (simulado)' >&2
                            exit 2
                        fi
                        if [[ "$entrada" == *"backend_type = 'client backend'"* ]]; then
                            echo "${STUB_CONEXIONES:-0}"
                        else
                            echo "$(( ${STUB_CONEXIONES:-0} + ${STUB_WORKERS:-0} ))"
                        fi
                        ;;
                    *"version()"*) echo 'PostgreSQL 16.4 on x86_64-pc-linux-musl (simulado)' ;;
                    *MigrationId*) echo '20261001000000_UltimaSimulada' ;;
                    *information_schema.tables*) printf 'Categorias|3\nLogsAuditoria|10\n__EFMigrationsHistory|25\n' ;;
                    *) echo "stub psql: SQL no prevista: $entrada" >&2; exit 99 ;;
                esac
                exit 0
                ;;
            *"pg_dump --version"*)
                echo 'pg_dump (PostgreSQL) 16.4'
                exit 0
                ;;
            *"pg_dump"*)
                exit 0
                ;;
            *"sha256sum /tmp/volcado-"*)
                sha="${STUB_SHA_REMOTO:-$(contenido | sha256sum | cut -d' ' -f1)}"
                echo "$sha  /tmp/volcado-x.dump"
                exit 0
                ;;
            *"stat -c %s /tmp/volcado-"*)
                echo "${STUB_TAMANO_DUMP:-2048}"
                exit 0
                ;;
            *"rm -f /tmp/volcado-"*)
                exit 0
                ;;
            *"cat /tmp/volcado-"*)
                contenido
                exit 0
                ;;
            "hostname")
                echo 'vps-viejo-simulado'
                exit 0
                ;;
            *"tar -C"*)
                if [[ -n "${STUB_TAR_CORRUPTO:-}" ]]; then echo 'esto no es un tar'; exit 0; fi
                tar -C "$STUB_DIR_TAR" -cpf - backups
                exit 0
                ;;
            *"sed -n"*"api.env"*)
                printf 'Jwt__Secret\nConnectionStrings__Default\nRateLimiting__Login__PermitLimit\nPOSTGRES_USER\n'
                exit 0
                ;;
            *)
                echo "stub ssh: comando no previsto: $cmd" >&2
                exit 99
                ;;
        esac
        """;

    private const string ScpFalso = """
        #!/usr/bin/env bash
        { printf 'SCP:'; printf ' %s' "$@"; printf '\n'; } >> "$RASTRO"
        exit 0
        """;

    private sealed record Resultado(int ExitCode, string Stdout, string Stderr, string Rastro, string Salida, string Dir)
    {
        public string Estado(string archivo) => File.ReadAllText(Path.Combine(Dir, archivo)).Trim();
        public string[] LineasRastro => Rastro.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        public string Diag => $"exit={ExitCode}\nstdout={Stdout}\nstderr={Stderr}\nrastro=\n{Rastro}";
    }

    private static void EscribirEjecutable(string ruta, string contenido)
    {
        File.WriteAllText(ruta, contenido);
        File.SetUnixFileMode(ruta, (UnixFileMode)0b111_101_101);
    }

    /// <summary>
    /// Corre el script real. Por defecto: .env de prueba vía STOCKAPP_ENV_LOCAL, VPS_* de prueba,
    /// API activa y habilitada. <paramref name="sinVariable"/> deja ausente una de VPS_HOST/USER/
    /// SSH_PORT; <paramref name="envLocal"/> reemplaza la ruta de STOCKAPP_ENV_LOCAL.
    /// </summary>
    private Resultado Correr(
        string args = "", IDictionary<string, string>? env = null, string estadoInicial = "active",
        string? sinVariable = null, string? envLocal = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "volcado-test-" + Guid.NewGuid().ToString("N")[..12]);
        _directorios.Add(dir);
        var testbin = Path.Combine(dir, "testbin");
        var dirTar = Path.Combine(dir, "remoto-backups");
        Directory.CreateDirectory(testbin);
        Directory.CreateDirectory(Path.Combine(dirTar, "backups"));
        File.WriteAllText(Path.Combine(dirTar, "backups", "backup_20261001_000000000.dump"), "dump viejo");
        EscribirEjecutable(Path.Combine(testbin, "ssh"), SshFalso);
        EscribirEjecutable(Path.Combine(testbin, "scp"), ScpFalso);

        var archivoEnv = Path.Combine(dir, "operador.env");
        File.WriteAllText(archivoEnv, "POSTGRES_USER=stockapp\nPOSTGRES_DB=stockapp\n");
        File.WriteAllText(Path.Combine(dir, "estado"), estadoInicial == "masked" ? "inactive\n" : estadoInicial + "\n");
        File.WriteAllText(Path.Combine(dir, "mascara"), estadoInicial == "masked" ? "masked\n" : "enabled\n");
        File.WriteAllText(Path.Combine(dir, "ps-contador"), "0\n");
        File.WriteAllText(Path.Combine(dir, "unit"), estadoInicial == "masked" ? "movida\n" : "regular\n");
        var rastro = Path.Combine(dir, "rastro");
        var salida = Path.Combine(dir, "salida");

        var vps = new Dictionary<string, string>
        {
            ["VPS_HOST"] = "vps-viejo.test.invalid", ["VPS_USER"] = "operador", ["VPS_SSH_PORT"] = "2222",
        };
        if (sinVariable is not null) vps.Remove(sinVariable);

        var script =
            "exec </dev/null\n" +
            // Higiene (ruling F2): nada del entorno de quien corre la suite se cuela en la corrida.
            "unset STOCKAPP_ENV_LOCAL\n" +
            "for v in $(compgen -v VPS_); do unset \"$v\"; done\n" +
            $"export PATH=\"{testbin}:$PATH\"\n" +
            $"export RASTRO=\"{rastro}\"\n" +
            $"export STUB_ESTADO=\"{dir}/estado\" STUB_MASCARA=\"{dir}/mascara\" STUB_PS_CONTADOR=\"{dir}/ps-contador\" STUB_UNIT=\"{dir}/unit\"\n" +
            $"export STUB_DIR_TAR=\"{dirTar}\"\n" +
            $"export STOCKAPP_ENV_LOCAL=\"{envLocal ?? archivoEnv}\"\n" +
            string.Concat(vps.Select(kv => $"export {kv.Key}=\"{kv.Value}\"\n")) +
            string.Concat((env ?? new Dictionary<string, string>()).Select(kv => $"export {kv.Key}=\"{kv.Value}\"\n")) +
            $"bash \"{RutaScriptReal()}\" --salida \"{salida}\" {args}";
        var (exit, stdout, stderr) = EjecutorBash.Ejecutar(script);
        var textoRastro = File.Exists(rastro) ? File.ReadAllText(rastro) : "";
        return new Resultado(exit, stdout, stderr, textoRastro, salida, dir);
    }

    private static int IndiceDe(Resultado r, string fragmento) =>
        Array.FindIndex(r.LineasRastro, l => l.Contains(fragmento));

    private static bool HuboPgDump(Resultado r) =>
        r.LineasRastro.Any(l => l.Contains("pg_dump") && !l.Contains("pg_dump --version"));

    private static string Sha256(string ruta) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ruta))).ToLowerInvariant();

    private static Dictionary<string, string> Env(params (string Clave, string Valor)[] pares) =>
        pares.ToDictionary(p => p.Clave, p => p.Valor);

    private static readonly string ShaDistinto = new('0', 64);

    // ---------- Congelamiento: stop + mask antes del dump ----------

    [Fact]
    public void ModoFinal_StopYMaskAntesDelPgDump()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        var stop = IndiceDe(r, "systemctl stop stockapp-api");
        var mask = IndiceDe(r, "systemctl -q mask stockapp-api");
        var dump = Array.FindIndex(r.LineasRastro, l => l.Contains("pg_dump") && !l.Contains("--version"));
        Assert.True(stop >= 0 && mask >= 0 && dump >= 0, r.Diag);
        Assert.True(stop < dump && mask < dump, r.Diag);
        Assert.Contains("OK", r.Stdout);
    }

    [Fact]
    public void ApiSigueActivaTrasStop_Aborta3SinDump()
    {
        var r = Correr(env: Env(("STUB_ACTIVA_TRAS_STOP", "1")));

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.False(HuboPgDump(r), r.Diag);
    }

    [Fact]
    public void ConexionesAbiertas_Aborta3SinDump()
    {
        var r = Correr(env: Env(("STUB_CONEXIONES", "2")));

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.False(HuboPgDump(r), r.Diag);
    }

    [Fact]
    public void ConsultaDeConexionesFalla_Aborta8NoLaTomaComoCero()
    {
        var r = Correr(env: Env(("STUB_FALLAR_CONEXIONES", "1")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.False(HuboPgDump(r), r.Diag);
    }

    [Fact]
    public void ConexionesNoNumerico_Aborta8()
    {
        var r = Correr(env: Env(("STUB_CONEXIONES", "x")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.False(HuboPgDump(r), r.Diag);
    }

    // ---------- Traída verificada ----------

    [Fact]
    public void ShaLocalDistinto_Aborta4YBorraElArchivoLocal()
    {
        var r = Correr(env: Env(("STUB_SHA_REMOTO", ShaDistinto)));

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.Empty(Directory.Exists(r.Salida) ? Directory.GetFiles(r.Salida, "volcado-*.dump") : []);
        Assert.Empty(Directory.Exists(r.Salida) ? Directory.GetFiles(r.Salida, "volcado-*.manifiesto") : []);
        Assert.DoesNotContain("OK", r.Stdout);
    }

    [Fact]
    public void DumpChico_Aborta4()
    {
        var r = Correr(env: Env(("STUB_TAMANO_DUMP", "100")));

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.Empty(Directory.Exists(r.Salida) ? Directory.GetFiles(r.Salida, "volcado-*.dump") : []);
        Assert.Empty(Directory.Exists(r.Salida) ? Directory.GetFiles(r.Salida, "volcado-*.manifiesto") : []);
    }

    // ---------- --ensayo: el trap deja la API como la encontró ----------

    [Fact]
    public void Ensayo_ApiEstabaActiva_ElTrapLaArrancaAunqueFalleAMitad()
    {
        var r = Correr("--ensayo", env: Env(("STUB_SHA_REMOTO", ShaDistinto)));

        Assert.True(r.ExitCode == 4, r.Diag);
        var ultimaSystemctl = r.LineasRastro.Last(l => l.Contains("systemctl"));
        Assert.Contains("systemctl start stockapp-api", ultimaSystemctl);
        Assert.DoesNotContain("systemctl mask", r.Rastro);
    }

    [Fact]
    public void Ensayo_ApiEstabaInactiva_ElTrapNoLaArranca()
    {
        var r = Correr("--ensayo", estadoInicial: "inactive");

        Assert.True(r.ExitCode == 0, r.Diag);
        Assert.DoesNotContain("systemctl start", r.Rastro);
    }

    [Fact]
    public void ModoFinal_NuncaStartNiUnmask()
    {
        var feliz = Correr();
        var shaDistinto = Correr(env: Env(("STUB_SHA_REMOTO", ShaDistinto)));

        Assert.True(feliz.ExitCode == 0, feliz.Diag);
        Assert.True(shaDistinto.ExitCode == 4, shaDistinto.Diag);
        foreach (var r in new[] { feliz, shaDistinto })
        {
            Assert.DoesNotContain("systemctl start", r.Rastro);
            Assert.DoesNotContain("unmask", r.Rastro);
        }
    }

    [Fact]
    public void ModoFinal_Reintento_ConApiYaEnmascarada_Funciona()
    {
        var r = Correr(estadoInicial: "masked");

        Assert.True(r.ExitCode == 0, r.Diag);
        Assert.DoesNotContain("sudo mv", r.Rastro);
        Assert.Equal("masked", r.Estado("mascara"));
        Assert.DoesNotContain("unmask", r.Rastro);
        Assert.DoesNotContain("systemctl start", r.Rastro);
        Assert.Single(Directory.GetFiles(r.Salida, "volcado-*.dump"));
    }

    // ---------- Alcance: VPS compartido ----------

    [Fact]
    public void TodoSystemctlNombraStockappApi_YTodoDockerNombraElContenedor()
    {
        foreach (var r in new[] { Correr("--ensayo"), Correr() })
            AfirmarAlcance(r);
    }

    private static void AfirmarAlcance(Resultado r)
    {
        Assert.True(r.ExitCode == 0, r.Diag);
        // Única excepción: 'daemon-reload' (relee las units del disco tras mover la de StockApp; no
        // cambia el estado de ninguna otra).
        foreach (var l in r.LineasRastro.Where(l => l.Contains("systemctl") && !l.EndsWith("sudo systemctl daemon-reload")))
            Assert.Contains("stockapp-api", l);
        foreach (var l in r.LineasRastro.Where(l => l.Contains("docker exec") || l.Contains("docker inspect")))
            Assert.Contains("stockapp-pg", l);
        var dockerPs = r.LineasRastro.Where(l => l.Contains("docker ps")).ToList();
        Assert.NotEmpty(dockerPs);
        Assert.All(dockerPs, l => Assert.EndsWith("docker ps --format '{{.Names}}'", l));
    }

    [Fact]
    public void ElDumpNuncaTocaUnaRutaDelHostRemoto()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        var conTmp = r.LineasRastro.Where(l => l.Contains("/tmp/")).ToList();
        Assert.NotEmpty(conTmp);
        Assert.All(conTmp, l => Assert.Contains("docker exec", l));
    }

    [Fact]
    public void ArchivoTemporalDelContenedor_SeBorraEnElTrap()
    {
        var feliz = Correr();
        var shaDistinto = Correr(env: Env(("STUB_SHA_REMOTO", ShaDistinto)));

        foreach (var r in new[] { feliz, shaDistinto })
            Assert.True(r.LineasRastro.Any(l => l.Contains("docker exec stockapp-pg rm -f /tmp/volcado-")), r.Diag);
    }

    // ---------- Lo que queda en --salida ----------

    [Fact]
    public void Overrides_ListaSoloNombresFueraDeLosConocidos()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        var overrides = File.ReadAllText(Path.Combine(r.Salida, "overrides-api-env.txt"));
        Assert.Equal("RateLimiting__Login__PermitLimit", overrides.Trim());
        Assert.DoesNotContain("=", overrides);
    }

    [Fact]
    public void Manifiesto_TieneLasClavesYLosConteos()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        var dump = Assert.Single(Directory.GetFiles(r.Salida, "volcado-*.dump"));
        var manifiesto = Assert.Single(Directory.GetFiles(r.Salida, "volcado-*.manifiesto"));
        Assert.Equal(Path.ChangeExtension(dump, ".manifiesto"), manifiesto);
        var lineas = File.ReadAllLines(manifiesto);

        Assert.Equal("# manifiesto-migracion v1", lineas[0]);
        var separador = Array.IndexOf(lineas, "[conteos]");
        Assert.True(separador > 0, string.Join('\n', lineas));
        var cabecera = lineas[1..separador].ToDictionary(l => l[..l.IndexOf('=')], l => l[(l.IndexOf('=') + 1)..]);
        foreach (var clave in new[] { "fecha_utc", "host_origen", "version_servidor", "version_pg_dump", "base", "dump",
                     "sha256", "tamano_bytes", "ultima_migracion" })
            Assert.True(cabecera.ContainsKey(clave) && cabecera[clave] != "", $"falta {clave}:\n{string.Join('\n', lineas)}");

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", cabecera["fecha_utc"]);
        Assert.Equal("vps-viejo-simulado", cabecera["host_origen"]);
        Assert.Equal("stockapp", cabecera["base"]);
        Assert.Equal(Path.GetFileName(dump), cabecera["dump"]);
        Assert.Equal(Sha256(dump), cabecera["sha256"]);
        Assert.Equal(new FileInfo(dump).Length.ToString(), cabecera["tamano_bytes"]);
        Assert.Equal("20261001000000_UltimaSimulada", cabecera["ultima_migracion"]);
        Assert.Equal(["Categorias\t3", "LogsAuditoria\t10", "__EFMigrationsHistory\t25"], lineas[(separador + 1)..]);
    }

    [Fact]
    public void Salida_Permisos700Y600()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        Assert.Equal((UnixFileMode)0b111_000_000, File.GetUnixFileMode(r.Salida));
        var archivos = Directory.GetFiles(r.Salida);
        Assert.Equal(4, archivos.Length);
        Assert.Contains(archivos, a => Path.GetFileName(a) == "backups-historial.tar");
        Assert.All(archivos, a => Assert.Equal((UnixFileMode)0b110_000_000, File.GetUnixFileMode(a)));
        var (exitTar, listado, _) = EjecutorBash.Ejecutar($"tar -tf \"{r.Salida}/backups-historial.tar\"");
        Assert.Equal(0, exitTar);
        Assert.Contains("backups/backup_20261001_000000000.dump", listado);
    }

    [Fact]
    public void DockerPsDistintoAlFinal_Aborta9()
    {
        var r = Correr(env: Env(("STUB_DOCKER_PS_FINAL_DISTINTO", "1")));

        Assert.True(r.ExitCode == 9, r.Diag);
    }

    // ---------- Precondiciones: abortan antes de tocar nada ----------

    [Fact]
    public void ContenedorFueraDePatron_Aborta3SinSsh()
    {
        var r = Correr("--contenedor pinar-postgres-1");

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void RaizRemotaConPuntoPunto_Aborta2()
    {
        var r = Correr("--raiz-remota /srv/ensayo/../etc");

        Assert.True(r.ExitCode == 2, r.Diag);
        Assert.Equal("", r.Rastro);
    }

    [Fact]
    public void SudoSinContrasena_Aborta3SinStop()
    {
        var r = Correr(env: Env(("STUB_SUDO_FALLA", "1")));

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.DoesNotContain("systemctl stop", r.Rastro);
    }

    [Theory]
    [InlineData("VPS_HOST")]
    [InlineData("VPS_USER")]
    [InlineData("VPS_SSH_PORT")]
    public void SinVariablesDeConexion_Aborta3SinSsh(string variable)
    {
        var r = Correr(sinVariable: variable);

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.Contains(variable, r.Stderr);
        Assert.Equal("", r.Rastro);
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

    // ---------- Congelamiento real: la unit de install.sh es un archivo regular en /etc ----------

    private const string UnitEtc = "/etc/systemd/system/stockapp-api.service";

    /// <summary>
    /// Lo que hacía la primera versión (mask directo) contra el layout que deja install.sh: el
    /// stub, fiel a systemd 255, lo rechaza. Este test fija esa fidelidad: si el stub vuelve a
    /// aceptar el mask directo, los tests de congelamiento vuelven a mentir.
    /// </summary>
    [Fact]
    public void StubFiel_MaskDirectoSobreUnitRegularFalla_ComoSystemd()
    {
        var dir = Path.Combine(Path.GetTempPath(), "volcado-stub-" + Guid.NewGuid().ToString("N")[..12]);
        _directorios.Add(dir);
        Directory.CreateDirectory(dir);
        EscribirEjecutable(Path.Combine(dir, "ssh"), SshFalso);
        File.WriteAllText(Path.Combine(dir, "unit"), "regular\n");
        File.WriteAllText(Path.Combine(dir, "mascara"), "enabled\n");

        var (exit, _, stderr) = EjecutorBash.Ejecutar(
            $"export RASTRO=/dev/null STUB_UNIT=\"{dir}/unit\" STUB_MASCARA=\"{dir}/mascara\"\n" +
            $"\"{dir}/ssh\" -p 1 x@y.invalid 'sudo systemctl mask stockapp-api' </dev/null");

        Assert.Equal(1, exit);
        Assert.Contains("already exists", stderr);
        Assert.Equal("enabled", File.ReadAllText(Path.Combine(dir, "mascara")).Trim());
    }

    [Fact]
    public void ModoFinal_MueveLaUnitRecargaYEnmascara_AntesDelDump()
    {
        var r = Correr();

        Assert.True(r.ExitCode == 0, r.Diag);
        Assert.Equal("masked", r.Estado("mascara"));
        Assert.Equal("movida", r.Estado("unit"));
        var orden = new[]
        {
            "systemctl stop stockapp-api",
            "systemctl show -p FragmentPath stockapp-api",
            $"sudo mv {UnitEtc} {UnitEtc}.congelado",
            "sudo systemctl daemon-reload",
            "sudo systemctl -q mask stockapp-api",
        }.Select(f => IndiceDe(r, f)).ToList();
        Assert.True(orden.All(i => i >= 0), r.Diag);
        Assert.Equal(orden.OrderBy(i => i).ToList(), orden);
        var dump = Array.FindIndex(r.LineasRastro, l => l.Contains("pg_dump") && !l.Contains("--version"));
        Assert.True(orden.Last() < dump, r.Diag);
        // Al terminar, el operador sabe en qué estado quedó la API y cómo volver atrás.
        Assert.Contains("sudo systemctl unmask stockapp-api", r.Stdout);
    }

    [Fact]
    public void FragmentPathDistinto_Aborta8SinMoverNiEnmascarar()
    {
        var r = Correr(env: Env(("STUB_FRAGMENT", "/usr/lib/systemd/system/stockapp-api.service")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.DoesNotContain("sudo mv", r.Rastro);
        Assert.DoesNotContain("mask stockapp-api", r.Rastro);
        Assert.False(HuboPgDump(r), r.Diag);
        Assert.Contains("/usr/lib/systemd/system/stockapp-api.service", r.Stderr);
    }

    [Fact]
    public void CongeladoYaExiste_Aborta8SinPisarlo()
    {
        var r = Correr(env: Env(("STUB_CONGELADO_EXISTE", "1")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.DoesNotContain("sudo mv", r.Rastro);
        Assert.False(HuboPgDump(r), r.Diag);
    }

    /// <summary>
    /// Reintento tras una corrida a medias: la unit ya se movió a .congelado (systemd todavía la
    /// reporta en /etc porque no hubo daemon-reload). El aborto tiene que dar la vuelta atrás de
    /// una unit movida (mv, daemon-reload, start), no solo el start.
    /// </summary>
    [Fact]
    public void CongeladoYaExiste_InformaLaVueltaAtrasDeUnaUnitMovida()
    {
        var r = Correr(env: Env(("STUB_CONGELADO_EXISTE", "1")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.Contains($"sudo mv {UnitEtc}.congelado {UnitEtc}", r.Stderr);
        Assert.Contains("sudo systemctl daemon-reload", r.Stderr);
        Assert.Contains("sudo systemctl start stockapp-api", r.Stderr);
        Assert.DoesNotContain("unmask", r.Stderr);
    }

    /// <summary>
    /// Un comando que HIZO efecto pero dejó algo en stderr aborta (remoto_ok), y el mensaje tiene
    /// que reflejar el efecto: las banderas se ponen ANTES de ejecutar.
    /// </summary>
    [Fact]
    public void MvConEfectoYStderr_InformaLaUnitMovida()
    {
        var r = Correr(env: Env(("STUB_MV_ADVIERTE", "1")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.Equal("movida", r.Estado("unit"));
        Assert.Contains($"sudo mv {UnitEtc}.congelado {UnitEtc}", r.Stderr);
        Assert.Contains("sudo systemctl daemon-reload", r.Stderr);
    }

    [Fact]
    public void MaskConEfectoYStderr_InformaQueQuedoEnmascarada()
    {
        var r = Correr(env: Env(("STUB_MASK_ADVIERTE", "1")));

        Assert.True(r.ExitCode == 8, r.Diag);
        Assert.Equal("masked", r.Estado("mascara"));
        Assert.Contains("ENMASCARADA", r.Stderr);
        Assert.Contains("sudo systemctl unmask stockapp-api", r.Stderr);
    }

    // ---------- I1: en modo final, todo fallo después del stop informa el estado y R-A ----------

    public static TheoryData<string, string, bool> FallosDespuesDelStop() => new()
    {
        // (variable del stub, valor, ¿la unit llegó a moverse y enmascararse?)
        { "STUB_FRAGMENT", "/usr/lib/systemd/system/stockapp-api.service", false },
        { "STUB_ACTIVA_TRAS_STOP", "1", true },
        { "STUB_CONEXIONES", "2", true },
        { "STUB_SHA_REMOTO", new string('0', 64), true },
        { "STUB_TAMANO_DUMP", "100", true },
        { "STUB_TAR_CORRUPTO", "1", true },
        { "STUB_DOCKER_PS_FINAL_DISTINTO", "1", true },
    };

    [Theory]
    [MemberData(nameof(FallosDespuesDelStop))]
    public void ModoFinal_FalloDespuesDelStop_InformaEstadoYComandosDeVueltaAtras(string variable, string valor, bool congelada)
    {
        var r = Correr(env: Env((variable, valor)));

        Assert.True(r.ExitCode != 0, r.Diag);
        Assert.Contains("stockapp-api queda DETENIDA", r.Stderr);
        Assert.Contains("sudo systemctl start stockapp-api", r.Stderr);
        if (congelada)
        {
            Assert.Contains("ENMASCARADA", r.Stderr);
            Assert.Contains("sudo systemctl unmask stockapp-api", r.Stderr);
            Assert.Contains($"sudo mv {UnitEtc}.congelado {UnitEtc}", r.Stderr);
            Assert.Contains("sudo systemctl daemon-reload", r.Stderr);
        }
        else
        {
            Assert.DoesNotContain("unmask", r.Stderr);
        }
    }

    [Fact]
    public void FalloAntesDelStop_NoHablaDeVueltaAtras()
    {
        var r = Correr(env: Env(("STUB_SUDO_FALLA", "1")));

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.DoesNotContain("queda DETENIDA", r.Stderr);
    }

    [Fact]
    public void Ensayo_Fallo_NoHablaDeVueltaAtras()
    {
        var r = Correr("--ensayo", env: Env(("STUB_SHA_REMOTO", ShaDistinto)));

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.DoesNotContain("queda DETENIDA", r.Stderr);
    }

    // ---------- Menores ----------

    [Fact]
    public void WorkerQueNoEsCliente_NoDaExit3()
    {
        var r = Correr(env: Env(("STUB_WORKERS", "1")));

        Assert.True(r.ExitCode == 0, r.Diag);
    }

    [Fact]
    public void SudoConAdvertenciaEnStderr_Aborta3SinStop()
    {
        var r = Correr(env: Env(("STUB_SUDO_ADVIERTE", "1")));

        Assert.True(r.ExitCode == 3, r.Diag);
        Assert.DoesNotContain("systemctl stop", r.Rastro);
        Assert.Contains("unable to resolve host", r.Stderr);
    }

    [Fact]
    public void HistorialTarIlegible_Aborta4YLoBorra()
    {
        var r = Correr(env: Env(("STUB_TAR_CORRUPTO", "1")));

        Assert.True(r.ExitCode == 4, r.Diag);
        Assert.False(File.Exists(Path.Combine(r.Salida, "backups-historial.tar")));
    }
}
