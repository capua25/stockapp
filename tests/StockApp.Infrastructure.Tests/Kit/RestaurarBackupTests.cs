using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Npgsql;
using StockApp.Infrastructure.Backups;
using StockApp.Infrastructure.Tests.Fixtures;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/restaurar-backup.sh: restaura un dump -Fc en una base VACÍA, dentro del
/// contenedor destino, en una sola transacción, y nunca pisa datos existentes.
///
/// Corre el script REAL del repo contra un Postgres REAL: cada test levanta su propio contenedor
/// descartable (<see cref="ContenedorPg"/>, nombre "stockapp-test-&lt;guid12&gt;", que matchea el
/// patrón ^stockapp[a-z0-9-]*$ del script) y lo borra al terminar. Nunca se toca el stockapp-pg de
/// desarrollo ni ningún otro contenedor de la máquina: el script recibe --contenedor explícito.
///
/// Stubs por PATH (testbin): 'systemctl' falso (la API "activa" o no, a elección del test) y un
/// envoltorio de 'docker' que deja rastro de cada invocación y delega en el docker REAL -- salvo
/// cuando el test le pide simular que psql o pg_dump fallan. El dump "real" sale de la base
/// sembrada por <see cref="PostgresFixture"/> con <see cref="EjecutorPgDumpProceso"/> (el mismo
/// ejecutor que usa producción), así que tiene el esquema real de StockApp.
/// </summary>
[Collection("Postgres")]
public class RestaurarBackupTests
{
    private readonly PostgresFixture _fixture;

    public RestaurarBackupTests(PostgresFixture fixture) => _fixture = fixture;

    private static string RutaScriptReal([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "deploy", "restaurar-backup.sh"));
    }

    // ---------- La consulta de conteos, tomada del script (entre los marcadores) ----------

    /// <summary>
    /// SQL de conteos extraída del TEXTO del script, entre "# >>> SQL-CONTEOS" y
    /// "# &lt;&lt;&lt; SQL-CONTEOS" (dentro del heredoc &lt;&lt;'SQL'). Los tests cuentan con la MISMA
    /// consulta que el script, no con una copia propia que podría divergir.
    /// </summary>
    internal static string SqlDeConteos()
    {
        var lineas = File.ReadAllLines(RutaScriptReal());
        var inicio = Array.FindIndex(lineas, l => l.Trim() == "# >>> SQL-CONTEOS");
        var fin = Array.FindIndex(lineas, l => l.Trim() == "# <<< SQL-CONTEOS");
        Assert.True(inicio >= 0 && fin > inicio, "No se encontraron los marcadores SQL-CONTEOS en el script.");
        var bloque = lineas[(inicio + 1)..fin];
        var abre = Array.FindIndex(bloque, l => l.TrimEnd().EndsWith("<<'SQL'"));
        var cierra = Array.FindIndex(bloque, l => l == "SQL");
        Assert.True(abre >= 0 && cierra > abre, "El bloque SQL-CONTEOS no tiene el heredoc <<'SQL' ... SQL esperado.");
        return string.Join('\n', bloque[(abre + 1)..cierra]);
    }

    // ---------- Contenedor Postgres descartable ----------

    internal sealed class ContenedorPg : IDisposable
    {
        public string Nombre { get; } = "stockapp-test-" + Guid.NewGuid().ToString("N")[..12];

        public ContenedorPg()
        {
            var (exit, stdout, stderr) = EjecutorBash.Ejecutar(
                $"docker run -d --name {Nombre} -e POSTGRES_USER=stockapp -e POSTGRES_PASSWORD=test " +
                "-e POSTGRES_DB=stockapp postgres:16-alpine");
            if (exit != 0)
            {
                Dispose();
                throw new InvalidOperationException($"docker run falló: {stdout}\n{stderr}");
            }

            // Por TCP (-h 127.0.0.1), no por socket: el entrypoint de la imagen arranca primero un
            // servidor TEMPORAL solo por socket para initdb y lo reinicia. Por socket, pg_isready
            // da "listo" durante esa fase y el siguiente comando choca con el reinicio.
            var listo = EsperaMonotonica.HastaAsync(
                () => EjecutorBash.Ejecutar(
                    $"docker exec {Nombre} pg_isready -h 127.0.0.1 -U stockapp -d stockapp").ExitCode == 0,
                TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult();
            if (!listo)
            {
                Dispose();
                throw new InvalidOperationException($"Postgres no quedó listo en 60 s en {Nombre}.");
            }
        }

        /// <summary>Corre SQL (por stdin) con psql dentro del contenedor; lanza si falla.</summary>
        public string Psql(string sql, string @base = "stockapp")
        {
            var (exit, stdout, stderr) = EjecutorBash.Ejecutar(
                $"docker exec -i {Nombre} psql -U stockapp -d {@base} -tA -v ON_ERROR_STOP=1 <<'__SQL_TEST__'\n{sql}\n__SQL_TEST__");
            if (exit != 0)
                throw new InvalidOperationException($"psql falló en {Nombre}/{@base}: {stderr}\n{stdout}");
            return stdout;
        }

        /// <summary>Corre un comando en el contenedor; lanza si falla.</summary>
        public string Exec(string comando)
        {
            var (exit, stdout, stderr) = EjecutorBash.Ejecutar($"docker exec {Nombre} {comando}");
            if (exit != 0)
                throw new InvalidOperationException($"docker exec {comando} falló: {stderr}\n{stdout}");
            return stdout;
        }

        /// <summary>Salida de la consulta de conteos del script sobre la base indicada.</summary>
        public string Snapshot(string @base = "stockapp") => Psql(SqlDeConteos(), @base);

        /// <summary>Tablas en el schema public de la base indicada.</summary>
        public int TablasEnPublic(string @base = "stockapp") => int.Parse(Psql(
            "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';", @base));

        /// <summary>
        /// Arma un dump -Fc DENTRO del contenedor a partir de una base 'origen' preparada por
        /// <paramref name="preparar"/>, lo trae al host y borra la base y el archivo del contenedor.
        /// </summary>
        public string DumpDeOrigen(string directorio, Action<ContenedorPg> preparar)
        {
            Psql("CREATE DATABASE origen;");
            preparar(this);
            var dentro = "/tmp/origen-test.dump";
            Exec($"pg_dump -U stockapp -d origen --format=custom -f {dentro}");
            var afuera = Path.Combine(directorio, "origen-" + Guid.NewGuid().ToString("N")[..8] + ".dump");
            var (exit, _, stderr) = EjecutorBash.Ejecutar($"docker cp {Nombre}:{dentro} \"{afuera}\"");
            if (exit != 0) throw new InvalidOperationException($"docker cp falló: {stderr}");
            Exec($"rm -f {dentro}");
            Psql("DROP DATABASE origen;");
            return afuera;
        }

        /// <summary>Restaura un dump del host en una base del contenedor (pg_restore adentro).</summary>
        public void RestaurarEnBase(string dumpHost, string @base, bool crear = true)
        {
            if (crear) Psql($"CREATE DATABASE {@base};");
            var dentro = $"/tmp/restaurar-en-{@base}.dump";
            var (exit, _, stderr) = EjecutorBash.Ejecutar($"docker cp \"{dumpHost}\" {Nombre}:{dentro}");
            if (exit != 0) throw new InvalidOperationException($"docker cp falló: {stderr}");
            Exec($"pg_restore --no-owner --no-privileges -U stockapp -d {@base} {dentro}");
            Exec($"rm -f {dentro}");
        }

        public void Dispose() => EjecutorBash.Ejecutar($"docker rm -f -v {Nombre}");
    }

    // ---------- Dump con el esquema real (una vez por clase) ----------

    internal sealed record DumpReal(
        string Ruta, IReadOnlyDictionary<string, long> Conteos, string Sha256, long Tamano, string UltimaMigracion);

    private static readonly SemaphoreSlim CandadoDumpReal = new(1, 1);
    private static DumpReal? _dumpReal;
    private static readonly string DirectorioDeClase =
        Path.Combine(Path.GetTempPath(), "stockapp-restaurar-tests-" + Guid.NewGuid().ToString("N")[..8]);

    static RestaurarBackupTests()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Directory.Exists(DirectorioDeClase)) Directory.Delete(DirectorioDeClase, recursive: true);
        };
    }

    private async Task<DumpReal> DumpEsquemaReal()
    {
        await CandadoDumpReal.WaitAsync();
        try
        {
            if (_dumpReal is not null) return _dumpReal;

            Directory.CreateDirectory(DirectorioDeClase);
            var ruta = Path.Combine(DirectorioDeClase, "esquema-real.dump");
            var ejecutor = new EjecutorPgDumpProceso(new ConfiguracionVacia(), NullLogger<EjecutorPgDumpProceso>.Instance);
            var resultado = await ejecutor.EjecutarAsync(_fixture.ConnectionString, ruta, CancellationToken.None);
            Assert.True(resultado.Exitoso, $"pg_dump falló: {resultado.MensajeError}");

            // Los conteos se leen INMEDIATAMENTE después del dump, con la consulta del script: la
            // colección "Postgres" corre en serie, así que nadie escribe la fixture en el medio.
            var conteos = new Dictionary<string, long>();
            string ultimaMigracion;
            await using (var conexion = new NpgsqlConnection(_fixture.ConnectionString))
            {
                await conexion.OpenAsync();
                await using (var cmd = new NpgsqlCommand(SqlDeConteos(), conexion))
                await using (var lector = await cmd.ExecuteReaderAsync())
                {
                    while (await lector.ReadAsync())
                        conteos[lector.GetString(0)] = lector.GetInt64(1);
                }
                await using var cmdMig = new NpgsqlCommand(
                    "SELECT max(\"MigrationId\") FROM \"__EFMigrationsHistory\";", conexion);
                ultimaMigracion = (string)(await cmdMig.ExecuteScalarAsync())!;
            }

            var bytes = await File.ReadAllBytesAsync(ruta);
            _dumpReal = new DumpReal(
                ruta, conteos, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes.LongLength, ultimaMigracion);
            return _dumpReal;
        }
        finally
        {
            CandadoDumpReal.Release();
        }
    }

    private static Dictionary<string, long> ParsearSnapshot(string snapshot) =>
        snapshot.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('|'))
            .ToDictionary(p => p[0], p => long.Parse(p[1]));

    // ---------- Stubs por PATH ----------

    private const string SystemctlFalso = """
        #!/usr/bin/env bash
        { printf 'ARGS:'; printf ' %q' "$@"; printf '\n'; } >> "${RASTRO_SYSTEMCTL:-/dev/null}"
        if [[ "${1:-}" == "is-active" ]]; then
            if [[ "${STUB_SYSTEMCTL_ACTIVO:-}" == "1" ]]; then echo active; exit 0; fi
            echo inactive; exit 3
        fi
        echo "systemctl falso: subcomando no previsto: $*" >&2
        exit 1
        """;

    // Fallo inyectado por SUBCADENA sobre los args unidos (no por arg exacto): así también cubre
    // un pg_dump que viaja dentro de "sh -c '...'".
    private const string DockerEnvoltorio = """
        #!/usr/bin/env bash
        { printf 'ARGS:'; printf ' %q' "$@"; printf '\n'; } >> "${RASTRO_DOCKER:-/dev/null}"
        todos="$*"
        if [[ "${STUB_DOCKER_FALLAR_PSQL:-}" == "1" && "$todos" == *psql* ]]; then
            echo 'psql: error: simulado' >&2; exit 2
        fi
        if [[ "${STUB_DOCKER_FALLAR_PGDUMP:-}" == "1" && "$todos" == *pg_dump* ]]; then
            echo 'pg_dump: error: simulado' >&2; exit 2
        fi
        exec "$DOCKER_REAL" "$@"
        """;

    private sealed record Resultado(int ExitCode, string Stdout, string Stderr, string RastroDocker, string RastroSystemctl, string Home);

    /// <summary>
    /// Corre el script REAL con HOME propio (sin ~/stockapp-deploy/.env salvo que el test lo
    /// cree) y los stubs al frente del PATH. Higiene (ruling F2): se borran del entorno heredado
    /// STOCKAPP_ENV_LOCAL y toda VPS_* antes de aplicar <paramref name="env"/>.
    /// </summary>
    private Resultado Correr(string args, IDictionary<string, string>? env = null, string? home = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "restaurar-run-" + Guid.NewGuid().ToString("N")[..12]);
        var testbin = Path.Combine(dir, "testbin");
        home ??= Path.Combine(dir, "home");
        Directory.CreateDirectory(testbin);
        Directory.CreateDirectory(home);
        var systemctl = Path.Combine(testbin, "systemctl");
        var docker = Path.Combine(testbin, "docker");
        File.WriteAllText(systemctl, SystemctlFalso);
        File.WriteAllText(docker, DockerEnvoltorio);
        File.SetUnixFileMode(systemctl, (UnixFileMode)0b111_101_101);
        File.SetUnixFileMode(docker, (UnixFileMode)0b111_101_101);
        var rastroDocker = Path.Combine(dir, "rastro-docker");
        var rastroSystemctl = Path.Combine(dir, "rastro-systemctl");
        try
        {
            var script =
                "unset STOCKAPP_ENV_LOCAL\n" +
                "for v in $(compgen -v VPS_); do unset \"$v\"; done\n" +
                "export DOCKER_REAL=\"$(command -v docker)\"\n" +
                $"export HOME=\"{home}\"\n" +
                $"export PATH=\"{testbin}:$PATH\"\n" +
                $"export RASTRO_DOCKER=\"{rastroDocker}\"\n" +
                $"export RASTRO_SYSTEMCTL=\"{rastroSystemctl}\"\n" +
                string.Concat((env ?? new Dictionary<string, string>()).Select(kv => $"export {kv.Key}=\"{kv.Value}\"\n")) +
                $"bash \"{RutaScriptReal()}\" {args}";
            var (exit, stdout, stderr) = EjecutorBash.Ejecutar(script);
            return new Resultado(exit, stdout, stderr,
                File.Exists(rastroDocker) ? File.ReadAllText(rastroDocker) : "",
                File.Exists(rastroSystemctl) ? File.ReadAllText(rastroSystemctl) : "",
                home);
        }
        finally
        {
            Directory.Delete(testbin, recursive: true);
            if (File.Exists(rastroDocker)) File.Delete(rastroDocker);
            if (File.Exists(rastroSystemctl)) File.Delete(rastroSystemctl);
        }
    }

    private static string Base(ContenedorPg c) => $"--contenedor {c.Nombre} --base stockapp --usuario stockapp";

    private static string Diag(Resultado r) =>
        $"exit={r.ExitCode}\nstdout={r.Stdout}\nstderr={r.Stderr}\nrastro docker={r.RastroDocker}";

    private static string NuevoDirectorio()
    {
        var d = Path.Combine(Path.GetTempPath(), "restaurar-datos-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(d);
        return d;
    }

    // =====================================================================================
    // Precondiciones: nada se toca si algo no cierra
    // =====================================================================================

    [Fact]
    public async Task ContenedorFueraDePatron_Aborta3SinLlamarADocker()
    {
        var d = await DumpEsquemaReal();
        var r = Correr($"\"{d.Ruta}\" --contenedor postgres-otro --base stockapp --usuario stockapp");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.Contains("postgres-otro", r.Stderr);
        Assert.Equal("", r.RastroDocker);
    }

    [Fact]
    public async Task NombreDeBaseInvalido_Aborta2()
    {
        var d = await DumpEsquemaReal();
        var r = Correr($"\"{d.Ruta}\" --contenedor stockapp-test-nadie --base 'x;drop' --usuario stockapp");

        Assert.True(r.ExitCode == 2, Diag(r));
        Assert.Equal("", r.RastroDocker);
    }

    [Fact]
    public async Task ApiActiva_Aborta3SinTocarElContenedor()
    {
        var d = await DumpEsquemaReal();
        var r = Correr($"\"{d.Ruta}\" --contenedor stockapp-test-nadie --base stockapp --usuario stockapp",
            new Dictionary<string, string> { ["STUB_SYSTEMCTL_ACTIVO"] = "1" });

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.Contains("stockapp-api está activa", r.Stderr);
        Assert.Equal("", r.RastroDocker);
    }

    [Fact]
    public async Task ContenedorDetenido_Aborta3()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        EjecutorBash.Ejecutar($"docker stop {c.Nombre}");

        var r = Correr($"\"{d.Ruta}\" {Base(c)}");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.DoesNotContain("pg_restore", r.RastroDocker);
    }

    [Fact]
    public async Task BaseNoVacia_SinRecrear_Aborta3YNoTocaNada()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        c.Psql("CREATE TABLE \"LogsAuditoria\"(id int); INSERT INTO \"LogsAuditoria\" VALUES (1), (2), (3);");
        var antes = c.Snapshot();

        var r = Correr($"\"{d.Ruta}\" {Base(c)}");

        Assert.True(r.ExitCode == 3, Diag(r));
        Assert.Contains("LogsAuditoria: 3", r.Stderr);
        Assert.Contains("__EFMigrationsHistory: no existe", r.Stderr);
        Assert.Equal(antes, c.Snapshot());
    }

    [Fact]
    public async Task ConsultaQueFalla_NoSeInterpretaComoVacia_Aborta8()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();

        var r = Correr($"\"{d.Ruta}\" {Base(c)}",
            new Dictionary<string, string> { ["STUB_DOCKER_FALLAR_PSQL"] = "1" });

        Assert.True(r.ExitCode == 8, Diag(r));
        Assert.Contains("NO es 'base vacía'", r.Stderr);
        Assert.DoesNotContain("pg_restore", r.RastroDocker);
        Assert.Equal(0, c.TablasEnPublic());
    }

    // =====================================================================================
    // Camino feliz
    // =====================================================================================

    [Fact]
    public async Task BaseVacia_SinManifiesto_Restaura0()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();

        var r = Correr($"\"{d.Ruta}\" {Base(c)}");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal(d.Conteos, ParsearSnapshot(c.Snapshot()));
        Assert.Contains("API NO arrancada", r.Stdout);
    }

    [Fact]
    public async Task SinEnvPorDefecto_ConBaseYUsuarioExplicitos_NoLoNecesita()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();

        var r = Correr($"\"{d.Ruta}\" --contenedor {c.Nombre} --base stockapp --usuario stockapp");

        Assert.False(File.Exists(Path.Combine(r.Home, "stockapp-deploy", ".env")));
        Assert.True(r.ExitCode == 0, Diag(r));
    }

    [Fact]
    public async Task SinEnvYSinBase_Aborta2NombrandoElEnv()
    {
        var d = await DumpEsquemaReal();
        var r = Correr($"\"{d.Ruta}\" --contenedor stockapp-test-nadie");

        Assert.True(r.ExitCode == 2, Diag(r));
        Assert.Contains(Path.Combine(r.Home, "stockapp-deploy", ".env"), r.Stderr);
        Assert.Equal("", r.RastroDocker);
    }

    // =====================================================================================
    // Dumps rotos: la base nunca queda a medias
    // =====================================================================================

    [Fact]
    public async Task DumpCorruptoEnElEncabezado_Aborta4YLaBaseSigueVacia()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var corrupto = Path.Combine(dir, "corrupto.dump");
            await File.WriteAllBytesAsync(corrupto, (await File.ReadAllBytesAsync(d.Ruta))[..64]);

            var r = Correr($"\"{corrupto}\" {Base(c)}");

            Assert.True(r.ExitCode == 4, Diag(r));
            Assert.Equal(0, c.TablasEnPublic());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Dump propio armado dentro del contenedor: la tabla 'a' lleva ~1,3 MB poco comprimibles y
    /// 'z' viene después. Truncado al 60 %, el TOC (al principio del archivo) sigue legible --
    /// pg_restore --list sale 0 -- pero los datos se cortan a mitad: el pre-data ya creó 'a' y 'z'
    /// cuando falla. Sin --single-transaction esas tablas quedarían a medias.
    /// </summary>
    private static string DumpTruncado(ContenedorPg c, string dir)
    {
        var completo = c.DumpDeOrigen(dir, cont => cont.Psql(
            "CREATE TABLE a(id int generated by default as identity primary key, t text);" +
            "INSERT INTO a(t) SELECT string_agg(md5(random()::text), '') FROM generate_series(1, 40000);" +
            "CREATE TABLE z(id int);", "origen"));
        var bytes = File.ReadAllBytes(completo);
        var truncado = Path.Combine(dir, "truncado.dump");
        File.WriteAllBytes(truncado, bytes[..(int)(bytes.Length * 0.6)]);

        var (exitList, _, stderrList) = EjecutorBash.Ejecutar($"pg_restore --list \"{truncado}\" > /dev/null");
        Assert.True(exitList == 0,
            $"El dump truncado tiene que pasar 'pg_restore --list' (si no, el test no prueba un fallo A MITAD de restauración): {stderrList}");
        return truncado;
    }

    [Fact]
    public void FalloAMitadDeRestauracion_LaBaseQuedaVacia5()
    {
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var truncado = DumpTruncado(c, dir);

            var r = Correr($"\"{truncado}\" {Base(c)}");

            Assert.True(r.ExitCode == 5, Diag(r));
            Assert.Contains("la base quedó vacía", r.Stderr);
            Assert.Equal(0, c.TablasEnPublic());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ArchivoTemporalDelContenedor_SeBorraSiempre()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var truncado = DumpTruncado(c, dir);

            var fallo = Correr($"\"{truncado}\" {Base(c)}");
            Assert.True(fallo.ExitCode == 5, Diag(fallo));
            Assert.DoesNotContain("restaurar-", c.Exec("sh -c 'ls /tmp'"));

            var ok = Correr($"\"{d.Ruta}\" {Base(c)}");
            Assert.True(ok.ExitCode == 0, Diag(ok));
            Assert.DoesNotContain("restaurar-", c.Exec("sh -c 'ls /tmp'"));

            // --recrear-base deja además un dump de seguridad DENTRO del contenedor: también se borra.
            var recreada = Correr($"\"{d.Ruta}\" {Base(c)} --recrear-base stockapp --dir-seguridad \"{Path.Combine(dir, "seg")}\"");
            Assert.True(recreada.ExitCode == 0, Diag(recreada));
            var tmp = c.Exec("sh -c 'ls /tmp'");
            Assert.DoesNotContain("restaurar-", tmp);
            Assert.DoesNotContain("seguridad-", tmp);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // =====================================================================================
    // Manifiesto, conteos, secuencias y catálogos
    // =====================================================================================

    /// <summary>
    /// Escribe un manifiesto v1 (contrato compartido con volcado-final-vps.sh) a partir del dump
    /// real, con alteraciones opcionales para los casos de falla.
    /// </summary>
    internal static void EscribirManifiesto(
        string ruta, DumpReal d, Action<Dictionary<string, long>>? alterarConteos = null,
        string? ultimaMigracion = null, string? sha = null, long? tamano = null)
    {
        var conteos = new Dictionary<string, long>(d.Conteos);
        alterarConteos?.Invoke(conteos);
        var texto = new System.Text.StringBuilder();
        texto.Append("# manifiesto-migracion v1\n");
        texto.Append("fecha_utc=2026-10-20T13:05:00Z\n");
        texto.Append("host_origen=origen-de-prueba\n");
        texto.Append("version_servidor=PostgreSQL 16\n");
        texto.Append("version_pg_dump=pg_dump (PostgreSQL) 16\n");
        texto.Append("base=stockapp\n");
        texto.Append($"dump={Path.GetFileName(d.Ruta)}\n");
        texto.Append($"sha256={sha ?? d.Sha256}\n");
        texto.Append($"tamano_bytes={tamano ?? d.Tamano}\n");
        texto.Append($"ultima_migracion={ultimaMigracion ?? d.UltimaMigracion}\n");
        texto.Append("[conteos]\n");
        foreach (var (tabla, filas) in conteos) texto.Append($"{tabla}\t{filas}\n");
        File.WriteAllText(ruta, texto.ToString());
    }

    [Fact]
    public async Task ManifiestoCorrecto_Restaura0ConConteosIdenticos()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d);

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)}");

            Assert.True(r.ExitCode == 0, Diag(r));
            Assert.Equal(d.Conteos, ParsearSnapshot(c.Snapshot()));
            // Un par viejo pasa todos los chequeos: la fecha del volcado se imprime para que el operador la vea.
            Assert.Contains("se volcó el: 2026-10-20T13:05:00Z (UTC)", r.Stdout);
            foreach (var tabla in d.Conteos.Keys)
                Assert.Matches(new System.Text.RegularExpressions.Regex(
                    $@"^\s*{System.Text.RegularExpressions.Regex.Escape(tabla)}\s.*\bOK\b",
                    System.Text.RegularExpressions.RegexOptions.Multiline), r.Stdout);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ManifiestoConConteoAlterado_Falla6YLaBaseQuedaRestaurada()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, conteos => conteos["Categorias"] += 1);

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)}");

            Assert.True(r.ExitCode == 6, Diag(r));
            Assert.Contains($"Categorias: esperado {d.Conteos["Categorias"] + 1}, real {d.Conteos["Categorias"]}", r.Stderr);
            Assert.True(c.TablasEnPublic() > 0, "La base tiene que quedar restaurada (no se revierte).");
            Assert.Contains("API NO arrancada", r.Stderr + r.Stdout);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ManifiestoConMigracionDistinta_Falla6()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, ultimaMigracion: "20991231000000_MigracionQueNoExiste");

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)}");

            Assert.True(r.ExitCode == 6, Diag(r));
            Assert.Contains("20991231000000_MigracionQueNoExiste", r.Stderr);
            Assert.Contains(d.UltimaMigracion, r.Stderr);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ShaDistinto_Aborta4AntesDelDockerCp()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, sha: new string('0', 64));

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)}");

            Assert.True(r.ExitCode == 4, Diag(r));
            Assert.DoesNotContain(" cp ", r.RastroDocker);
            Assert.Equal(0, c.TablasEnPublic());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task TamanoDistinto_Aborta4()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, tamano: d.Tamano + 1);

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)}");

            Assert.True(r.ExitCode == 4, Diag(r));
            Assert.DoesNotContain(" cp ", r.RastroDocker);
            Assert.Equal(0, c.TablasEnPublic());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task TablasDelListNoCoincidenConElManifiesto_Aborta4()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, conteos => conteos["Fantasma"] = 0);

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)}");

            Assert.True(r.ExitCode == 4, Diag(r));
            Assert.Contains("Fantasma", r.Stderr);
            Assert.DoesNotContain("--single-transaction", r.RastroDocker);
            Assert.Equal(0, c.TablasEnPublic());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ManifiestoMalFormado_Aborta2()
    {
        var d = await DumpEsquemaReal();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d);
            File.WriteAllLines(manifiesto, File.ReadAllLines(manifiesto).Where(l => l != "[conteos]"));

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" --contenedor stockapp-test-nadie --base stockapp --usuario stockapp");

            Assert.True(r.ExitCode == 2, Diag(r));
            Assert.Equal("", r.RastroDocker);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Origen con el esquema real, una Categoria más y la secuencia de Categorias.Id un paso
    /// por DETRÁS del máximo: el primer alta tras la migración chocaría con la PK.
    /// </summary>
    [Fact]
    public async Task SecuenciaAtrasada_Falla6()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var g = Guid.NewGuid().ToString("N")[..8];
            var dump = c.DumpDeOrigen(dir, cont =>
            {
                cont.RestaurarEnBase(d.Ruta, "origen", crear: false);
                cont.Psql(
                    $"INSERT INTO \"Categorias\"(\"Nombre\") VALUES ('SeqA-{g}'), ('SeqB-{g}');" +
                    "SELECT setval(pg_get_serial_sequence('\"Categorias\"', 'Id'), (SELECT max(\"Id\") FROM \"Categorias\") - 1);",
                    "origen");
            });

            var r = Correr($"\"{dump}\" {Base(c)}");

            Assert.True(r.ExitCode == 6, Diag(r));
            Assert.Contains("Categorias.Id", r.Stderr);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task DuplicadosEnCatalogo_Falla7ConRemediacionSinEjecutarla()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            var dump = c.DumpDeOrigen(dir, cont =>
            {
                cont.RestaurarEnBase(d.Ruta, "origen", crear: false);
                cont.Psql(
                    "DROP INDEX \"IX_Categorias_Nombre\";" +
                    "INSERT INTO \"Categorias\"(\"Nombre\") VALUES ('Tornillos'), ('tornillos');",
                    "origen");
            });

            var r = Correr($"\"{dump}\" {Base(c)}");

            Assert.True(r.ExitCode == 7, Diag(r));
            Assert.Contains("UPDATE \"Categorias\"", r.Stderr);
            Assert.Contains("NO se ejecuta", r.Stderr);
            Assert.Equal("2", c.Psql("SELECT count(*) FROM \"Categorias\" WHERE lower(\"Nombre\") = 'tornillos';"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // =====================================================================================
    // --recrear-base: nunca se borra nada sin un dump de seguridad verificado
    // =====================================================================================

    private const string SqlPrevio =
        "CREATE TABLE previo(id int); INSERT INTO previo VALUES (1), (2), (3);";

    private static string[] DumpsDeSeguridad(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir) : [];

    [Fact]
    public async Task RecrearBase_NombreDistinto_Aborta2SinTocarNada()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            c.Psql(SqlPrevio);
            var antes = c.Snapshot();
            var seg = Path.Combine(dir, "seg");

            var r = Correr($"\"{d.Ruta}\" {Base(c)} --recrear-base otra --dir-seguridad \"{seg}\"");

            Assert.True(r.ExitCode == 2, Diag(r));
            Assert.Equal(antes, c.Snapshot());
            Assert.Empty(DumpsDeSeguridad(seg));
            Assert.Equal("", r.RastroDocker);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RecrearBase_TomaDumpDeSeguridadRestaurableYRestaura()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            c.Psql(SqlPrevio);
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d);
            var seg = Path.Combine(dir, "seg");

            var r = Correr($"\"{d.Ruta}\" --manifiesto \"{manifiesto}\" {Base(c)} --recrear-base stockapp --dir-seguridad \"{seg}\"");

            Assert.True(r.ExitCode == 0, Diag(r));
            var dumps = DumpsDeSeguridad(seg);
            Assert.Single(dumps);
            Assert.Matches(@"^pre-recrear-stockapp-\d{8}T\d{6}Z\.dump$", Path.GetFileName(dumps[0]));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(dumps[0]));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(seg));
            Assert.Contains(dumps[0], r.Stdout);

            // El dump de seguridad sirve: restaurado en otra base devuelve lo que había.
            c.RestaurarEnBase(dumps[0], "verif");
            Assert.Equal("3", c.Psql("SELECT count(*) FROM previo;", "verif"));

            // Y la base destino quedó con el contenido del dump, sin rastros de lo anterior.
            Assert.Equal(d.Conteos, ParsearSnapshot(c.Snapshot()));
            Assert.Equal("", c.Psql("SELECT to_regclass('public.previo');"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RecrearBase_FallaElDumpDeSeguridad_NoBorraNada9()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            c.Psql(SqlPrevio);
            var seg = Path.Combine(dir, "seg");

            var r = Correr($"\"{d.Ruta}\" {Base(c)} --recrear-base stockapp --dir-seguridad \"{seg}\"",
                new Dictionary<string, string> { ["STUB_DOCKER_FALLAR_PGDUMP"] = "1" });

            Assert.True(r.ExitCode == 9, Diag(r));
            Assert.Contains("pg_dump", r.RastroDocker);
            Assert.DoesNotContain("DROP", r.RastroDocker + r.Stdout);
            Assert.Equal("3", c.Psql("SELECT count(*) FROM previo;"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task RecrearBase_ConConexionAbierta_Aborta3YLaBaseQuedaIntacta()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();
        var dir = NuevoDirectorio();
        try
        {
            c.Psql(SqlPrevio);
            EjecutorBash.Ejecutar($"docker exec -d {c.Nombre} psql -U stockapp -d stockapp -c 'SELECT pg_sleep(120)'");
            // docker exec -d vuelve antes de que psql conecte: se espera a ver la sesión (ruling F14).
            var conectada = await EsperaMonotonica.HastaAsync(
                () => c.Psql("SELECT count(*) FROM pg_stat_activity WHERE query LIKE 'SELECT pg_sleep(120)%';") == "1",
                TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
            Assert.True(conectada, "La sesión con pg_sleep nunca apareció en pg_stat_activity.");

            var r = Correr($"\"{d.Ruta}\" {Base(c)} --recrear-base stockapp --dir-seguridad \"{Path.Combine(dir, "seg")}\"");

            Assert.True(r.ExitCode == 3, Diag(r));
            Assert.Contains("conexiones abiertas", r.Stderr);
            Assert.Equal("3", c.Psql("SELECT count(*) FROM previo;"));
        }
        finally
        {
            try
            {
                c.Psql("SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE query LIKE 'SELECT pg_sleep(120)%';", "postgres");
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }

    [Fact]
    public async Task ExitoSeguidoDeReintento_AbortaPorBaseNoVacia()
    {
        var d = await DumpEsquemaReal();
        using var c = new ContenedorPg();

        var primero = Correr($"\"{d.Ruta}\" {Base(c)}");
        Assert.True(primero.ExitCode == 0, Diag(primero));
        var antes = c.Snapshot();

        var segundo = Correr($"\"{d.Ruta}\" {Base(c)}");

        Assert.True(segundo.ExitCode == 3, Diag(segundo));
        Assert.Equal(antes, c.Snapshot());
    }

    // =====================================================================================
    // --verificar-post-arranque: después del primer arranque de la API (solo lectura)
    // =====================================================================================

    /// <summary>Contenedor con el dump real ya restaurado por el script y su manifiesto.</summary>
    private async Task<(ContenedorPg C, DumpReal D)> Restaurado()
    {
        var d = await DumpEsquemaReal();
        var c = new ContenedorPg();
        var r = Correr($"\"{d.Ruta}\" {Base(c)}");
        if (r.ExitCode != 0)
        {
            c.Dispose();
            Assert.Fail(Diag(r));
        }
        return (c, d);
    }

    private Resultado PostArranque(ContenedorPg c, string manifiesto) =>
        Correr($"--verificar-post-arranque --manifiesto \"{manifiesto}\" {Base(c)}");

    [Fact]
    public async Task PostArranque_SoloCreceEnTablasPermitidas_Ok()
    {
        var (c, d) = await Restaurado();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d);
            // Columnas obligatorias de LogAuditoria: UsuarioId (FK a Usuarios), Fecha, Accion,
            // Entidad, EntidadId, Detalle. Si la base no tiene usuarios (depende de qué sembró la
            // fixture), la FK se saltea solo en esta transacción: lo que se prueba es el conteo.
            c.Psql(
                "BEGIN;" +
                "SELECT set_config('session_replication_role', 'replica', true) WHERE NOT EXISTS (SELECT 1 FROM \"Usuarios\");" +
                "INSERT INTO \"LogsAuditoria\"(\"UsuarioId\", \"Fecha\", \"Accion\", \"Entidad\", \"EntidadId\", \"Detalle\") " +
                "VALUES (coalesce((SELECT min(\"Id\") FROM \"Usuarios\"), 1), now(), 1, 'PostArranque', 0, 'arranque');" +
                "COMMIT;");

            var r = PostArranque(c, manifiesto);

            Assert.True(r.ExitCode == 0, Diag(r));
            Assert.Equal("", r.RastroSystemctl);
        }
        finally { c.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task PostArranque_TablaComunConMasFilas_Falla6()
    {
        var (c, d) = await Restaurado();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d);
            c.Psql($"INSERT INTO \"Categorias\"(\"Nombre\") VALUES ('PostArranque-{Guid.NewGuid():N}');");

            var r = PostArranque(c, manifiesto);

            Assert.True(r.ExitCode == 6, Diag(r));
            Assert.Contains("Categorias", r.Stderr);
        }
        finally { c.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task PostArranque_LogsAuditoriaMenor_Falla6()
    {
        var (c, d) = await Restaurado();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, conteos => conteos["LogsAuditoria"] += 1);

            var r = PostArranque(c, manifiesto);

            Assert.True(r.ExitCode == 6, Diag(r));
            Assert.Contains("LogsAuditoria", r.Stderr);
        }
        finally { c.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// La retención de backups borra filas de CorridasBackup en el primer arranque (catch-up de
    /// BackupProgramadoService → ServicioBackup.AplicarRetencionAsync): no es pérdida de datos.
    /// </summary>
    [Fact]
    public async Task PostArranque_CorridasBackupMenor_Ok()
    {
        var (c, d) = await Restaurado();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, conteos => conteos["CorridasBackup"] += 2);

            var r = PostArranque(c, manifiesto);

            Assert.True(r.ExitCode == 0, Diag(r));
            Assert.Matches(@"CorridasBackup.*-2", r.Stdout);
        }
        finally { c.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task PostArranque_TablaNuevaNoEnManifiesto_OkYLaLista()
    {
        var (c, d) = await Restaurado();
        var dir = NuevoDirectorio();
        try
        {
            var manifiesto = Path.Combine(dir, "m.manifiesto");
            EscribirManifiesto(manifiesto, d, conteos => conteos.Remove("Zonas"));

            var r = PostArranque(c, manifiesto);

            Assert.True(r.ExitCode == 0, Diag(r));
            Assert.Matches(@"nuevas \(migraciones posteriores\)[\s\S]*Zonas", r.Stdout);
        }
        finally { c.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void PostArranque_SinManifiesto_Aborta2()
    {
        var r = Correr("--verificar-post-arranque --contenedor stockapp-test-nadie --base stockapp --usuario stockapp");

        Assert.True(r.ExitCode == 2, Diag(r));
        Assert.Contains("--manifiesto", r.Stderr);
        Assert.Equal("", r.RastroDocker);
    }

    /// <summary>
    /// Reemplazo de <c>new ConfigurationBuilder().Build()</c> sin agregar el paquete concreto
    /// (mismo criterio que RestaurabilidadBackupTests): toda clave devuelve null, así
    /// EjecutorPgDumpProceso resuelve "pg_dump" por PATH.
    /// </summary>
    private sealed class ConfiguracionVacia : IConfiguration
    {
        public string? this[string key]
        {
            get => null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
