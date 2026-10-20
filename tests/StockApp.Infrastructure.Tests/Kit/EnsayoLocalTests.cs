using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using StockApp.Infrastructure.Backups;
using StockApp.Infrastructure.Tests.Fixtures;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Colección propia, SIN paralelismo (ruling F9): el ensayo completo compara la lista de
/// contenedores de la máquina (docker ps) al principio y al final, igual que el volcado real.
/// Si otra colección creara o borrara contenedores en el medio, el guardián de alcance daría un
/// falso rojo. xUnit corre las colecciones con DisableParallelization después de las paralelas.
/// Tiene su propia <see cref="PostgresFixture"/> (de ahí sale el dump con el esquema real).
/// </summary>
[CollectionDefinition("EnsayoLocal", DisableParallelization = true)]
public sealed class EnsayoLocalCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>
/// Guardianes del ensayo local de la migración (deploy/ensayo/): el VPS viejo se simula en la
/// máquina del operador con shims de ssh/sudo/systemctl y contenedores propios
/// "stockapp-ensayo-*", para correr volcado-final-vps.sh y restaurar-backup.sh REALES de punta a
/// punta sin tocar ningún servidor ni ningún contenedor que no sea del ensayo.
///
/// Los tests nunca usan los nombres por defecto: cada uno pasa un --sufijo propio, así no chocan
/// con un ensayo que el operador tenga en curso.
/// </summary>
[Collection("EnsayoLocal")]
public class EnsayoLocalTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly List<string> _directorios = [];

    public EnsayoLocalTests(PostgresFixture fixture) => _fixture = fixture;

    public void Dispose()
    {
        foreach (var d in _directorios)
            if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
    }

    private static string RaizRepo([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
    }

    private static string Script => Path.Combine(RaizRepo(), "deploy", "ensayo", "ensayo-local.sh");
    private static string Shims => Path.Combine(RaizRepo(), "deploy", "ensayo", "shims");
    private static string Shim(string nombre) => Path.Combine(Shims, nombre);

    private string DirectorioNuevo(string prefijo)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefijo + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        _directorios.Add(dir);
        return dir;
    }

    /// <summary>
    /// Corre bash con higiene de entorno (ruling F2): nada de STOCKAPP_ENV_LOCAL, VPS_* ni
    /// ENSAYO_* de quien corre la suite se cuela en la corrida.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) Bash(string script) =>
        EjecutorBash.Ejecutar(
            "exec </dev/null\n" +
            "unset STOCKAPP_ENV_LOCAL\n" +
            "for v in $(compgen -v VPS_) $(compgen -v ENSAYO_); do unset \"$v\"; done\n" +
            script);

    private static string Diag((int ExitCode, string Stdout, string Stderr) r) =>
        $"exit={r.ExitCode}\nstdout={r.Stdout}\nstderr={r.Stderr}";

    /// <summary>Estado de systemctl simulado y raíz remota con la unit como archivo regular.</summary>
    private (string Estado, string Raiz) VpsViejoSimulado()
    {
        var dir = DirectorioNuevo("ensayo-shim-");
        var estado = Path.Combine(dir, "estado");
        var raiz = Path.Combine(dir, "vps-viejo");
        Directory.CreateDirectory(estado);
        Directory.CreateDirectory(Path.Combine(raiz, "etc", "systemd", "system"));
        File.WriteAllText(Path.Combine(estado, "stockapp-api.activo"), "active\n");
        File.WriteAllText(Path.Combine(estado, "stockapp-api.habilitado"), "enabled\n");
        File.WriteAllText(UnitEtc(raiz), "[Unit]\nDescription=unit simulada\n");
        return (estado, raiz);
    }

    private static string UnitEtc(string raiz) =>
        Path.Combine(raiz, "etc", "systemd", "system", "stockapp-api.service");

    private static string EnvShims(string estado, string raiz) =>
        $"export ENSAYO_ESTADO=\"{estado}\" ENSAYO_RAIZ=\"{raiz}\"\n";

    // ---------- shims/ssh ----------

    [Fact]
    public void ShimSsh_HostNoInvalid_Rechaza255()
    {
        var marca = Path.Combine(DirectorioNuevo("ensayo-ssh-"), "corrio");

        var r = Bash($"\"{Shim("ssh")}\" -p 22 -o BatchMode=yes operador@vps.ejemplo.com 'touch {marca}; echo corrio'");

        Assert.True(r.ExitCode == 255, Diag(r));
        Assert.Contains("ensayo: host no permitido", r.Stderr);
        Assert.DoesNotContain("corrio", r.Stdout);
        Assert.False(File.Exists(marca), "el comando remoto corrió con un host que no es .invalid");
    }

    [Fact]
    public void ShimSsh_CorreElComandoConLosShimsEnElPath()
    {
        var (estado, raiz) = VpsViejoSimulado();

        // PATH de la corrida SIN los shims: es el ssh falso el que tiene que ponerlos.
        var r = Bash(EnvShims(estado, raiz) +
            $"\"{Shim("ssh")}\" -p 22 -o BatchMode=yes e@x.ensayo.invalid 'sudo -n true && systemctl is-active stockapp-api'");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("active", r.Stdout.Trim());
    }

    [Fact]
    public void ShimSsh_HeredaStdinYUneLosArgumentosComoElSshReal()
    {
        var (estado, raiz) = VpsViejoSimulado();

        var r = Bash(EnvShims(estado, raiz) +
            $"printf 'hola\\n' | \"{Shim("ssh")}\" -p 22 e@x.ensayo.invalid cat '|' tr a-z A-Z");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("HOLA", r.Stdout.Trim());
    }

    // ---------- shims/systemctl ----------

    [Fact]
    public void ShimSystemctl_OtraUnit_Rechaza5()
    {
        var (estado, raiz) = VpsViejoSimulado();

        var r = Bash(EnvShims(estado, raiz) + $"\"{Shim("systemctl")}\" stop otra-app");

        Assert.True(r.ExitCode == 5, Diag(r));
        Assert.Equal("active", File.ReadAllText(Path.Combine(estado, "stockapp-api.activo")).Trim());
    }

    [Fact]
    public void ShimSystemctl_StopMaskYStartSobreMasked_Falla()
    {
        var (estado, raiz) = VpsViejoSimulado();
        var systemctl = $"\"{Shim("systemctl")}\"";
        File.Move(UnitEtc(raiz), UnitEtc(raiz) + ".congelado");

        var r = Bash(EnvShims(estado, raiz) +
            $"{systemctl} stop stockapp-api || exit 11\n" +
            $"{systemctl} -q mask stockapp-api || exit 12\n" +
            $"{systemctl} start stockapp-api");

        Assert.True(r.ExitCode == 1, Diag(r));
        Assert.Contains("Unit stockapp-api.service is masked.", r.Stderr);
        Assert.Equal("inactive", File.ReadAllText(Path.Combine(estado, "stockapp-api.activo")).Trim());

        var activa = Bash(EnvShims(estado, raiz) + $"{systemctl} is-active stockapp-api");
        Assert.True(activa.ExitCode == 3, Diag(activa));
        Assert.Equal("inactive", activa.Stdout.Trim());
        var habilitada = Bash(EnvShims(estado, raiz) + $"{systemctl} is-enabled stockapp-api");
        Assert.Equal("masked", habilitada.Stdout.Trim());
    }

    /// <summary>
    /// Fiel a systemd (verificado con systemd 255 en la Task 6): install.sh deja la unit como
    /// archivo REGULAR en /etc/systemd/system, y mask ahí falla porque el symlink a /dev/null va en
    /// esa misma ruta. Sin esto, el ensayo daría verde con una secuencia que en el VPS real falla.
    /// </summary>
    [Fact]
    public void ShimSystemctl_MaskSobreUnitRegular_FallaComoSystemd()
    {
        var (estado, raiz) = VpsViejoSimulado();

        var r = Bash(EnvShims(estado, raiz) + $"\"{Shim("systemctl")}\" -q mask stockapp-api");

        Assert.True(r.ExitCode == 1, Diag(r));
        Assert.Contains("already exists", r.Stderr);
        Assert.Equal("enabled", File.ReadAllText(Path.Combine(estado, "stockapp-api.habilitado")).Trim());
        Assert.Null(new FileInfo(UnitEtc(raiz)).LinkTarget);
    }

    [Fact]
    public void ShimSystemctl_MaskConLaUnitMovida_CreaElSymlinkYSinQAvisaEnStderr()
    {
        var (estado, raiz) = VpsViejoSimulado();
        var systemctl = $"\"{Shim("systemctl")}\"";
        var fragmento = Bash(EnvShims(estado, raiz) + $"{systemctl} show -p FragmentPath stockapp-api");
        Assert.True(fragmento.ExitCode == 0, Diag(fragmento));
        Assert.Equal($"FragmentPath={UnitEtc(raiz)}", fragmento.Stdout.Trim());
        File.Move(UnitEtc(raiz), UnitEtc(raiz) + ".congelado");

        var recarga = Bash(EnvShims(estado, raiz) + $"{systemctl} daemon-reload");
        var mask = Bash(EnvShims(estado, raiz) + $"{systemctl} mask stockapp-api");

        Assert.True(recarga.ExitCode == 0, Diag(recarga));
        Assert.True(mask.ExitCode == 0, Diag(mask));
        Assert.Contains("Created symlink", mask.Stderr);
        Assert.Equal("/dev/null", new FileInfo(UnitEtc(raiz)).LinkTarget);
        Assert.Equal("masked", Bash(EnvShims(estado, raiz) + $"{systemctl} is-enabled stockapp-api").Stdout.Trim());

        var unmask = Bash(EnvShims(estado, raiz) + $"{systemctl} unmask stockapp-api");

        Assert.True(unmask.ExitCode == 0, Diag(unmask));
        Assert.False(File.Exists(UnitEtc(raiz)) || new FileInfo(UnitEtc(raiz)).LinkTarget is not null,
            "unmask no sacó el symlink a /dev/null");
        Assert.Equal("enabled", Bash(EnvShims(estado, raiz) + $"{systemctl} is-enabled stockapp-api").Stdout.Trim());
    }

    // ---------- shims/sudo ----------

    [Fact]
    public void ShimSudo_NoEleva()
    {
        var (estado, raiz) = VpsViejoSimulado();

        var sudo = Bash($"\"{Shim("sudo")}\" -n id -u");
        var propio = Bash("id -u");
        var systemctl = Bash(EnvShims(estado, raiz) + $"\"{Shim("sudo")}\" -n systemctl is-active stockapp-api");

        Assert.True(sudo.ExitCode == 0, Diag(sudo));
        Assert.Equal(propio.Stdout.Trim(), sudo.Stdout.Trim());
        Assert.Equal("active", systemctl.Stdout.Trim());
    }

    // ---------- ensayo-local.sh: alcance ----------

    /// <summary>
    /// El ensayo corre en la máquina del operador, donde está el contenedor de desarrollo y los de
    /// otra aplicación: el script no los nombra NUNCA, y todo borrado nombra un literal del ensayo.
    /// </summary>
    [Fact]
    public void EnsayoLocal_SoloNombresDeEnsayo()
    {
        var texto = File.ReadAllText(Script);

        Assert.DoesNotContain("stockapp-pg", texto);
        Assert.DoesNotContain("pinar", texto);

        var borrados = texto.Split('\n')
            .Where(l => Regex.IsMatch(l, @"docker\s+(rm|volume\s+rm)\b"))
            .ToList();
        Assert.NotEmpty(borrados);
        foreach (var linea in borrados)
        {
            var m = Regex.Match(linea, @"docker\s+(?:rm(?:\s+-[a-z]+)*|volume\s+rm)\s+(\S+)");
            Assert.True(m.Success, $"No se entiende qué borra: {linea}");
            var objetivo = m.Groups[1].Value.Trim('"');
            Assert.True(objetivo.StartsWith("stockapp-ensayo-") || objetivo.StartsWith("stockapp_ensayo_"),
                $"Borrado que no nombra un literal del ensayo: {linea}");
        }
    }

    [Fact]
    public void Preparar_ConContenedorDeEnsayoExistente_AbortaSinTocarlo()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var nombre = $"stockapp-ensayo-pg-{sufijo}";
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-pre-"), "trabajo");
        var dump = Path.Combine(DirectorioNuevo("ensayo-dump-"), "real.dump");
        File.WriteAllText(dump, "no importa: aborta antes de mirarlo");
        try
        {
            var creado = Bash($"docker run -d --name {nombre} alpine sleep 300");
            Assert.True(creado.ExitCode == 0, Diag(creado));
            var id = Bash($"docker inspect -f '{{{{.Id}}}}' {nombre}").Stdout.Trim();

            var r = Bash($"\"{Script}\" preparar \"{dump}\" --trabajo \"{trabajo}\" --sufijo {sufijo} --puerto {PuertoLibre()}");

            Assert.True(r.ExitCode != 0, Diag(r));
            Assert.Contains(nombre, r.Stderr);
            Assert.Equal(id, Bash($"docker inspect -f '{{{{.Id}}}}' {nombre}").Stdout.Trim());
            Assert.Equal("", Bash(
                $"docker ps -a --format '{{{{.Names}}}}' | grep -x 'stockapp-ensayo-origen-pg-{sufijo}' || true").Stdout.Trim());
        }
        finally
        {
            // Por nombre EXACTO, también lo que un preparar roto hubiera creado con este sufijo.
            Bash($"docker rm -f -v {nombre} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
        }
    }

    [Fact]
    public void Preparar_PuertoOcupado_AbortaSinCrearNada()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-pre-"), "trabajo");
        var dump = Path.Combine(DirectorioNuevo("ensayo-dump-"), "real.dump");
        File.WriteAllText(dump, "no importa: aborta antes de mirarlo");
        var ocupado = new TcpListener(IPAddress.Loopback, 0);
        ocupado.Start();
        try
        {
            var puerto = ((IPEndPoint)ocupado.LocalEndpoint).Port;

            var r = Bash($"\"{Script}\" preparar \"{dump}\" --trabajo \"{trabajo}\" --sufijo {sufijo} --puerto {puerto}");

            Assert.True(r.ExitCode != 0, Diag(r));
            Assert.Contains(puerto.ToString(), r.Stderr);
            Assert.Equal("", Bash(
                $"docker ps -a --format '{{{{.Names}}}}' | grep -e '-{sufijo}$' || true").Stdout.Trim());
        }
        finally
        {
            ocupado.Stop();
        }
    }

    private static int PuertoLibre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var puerto = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return puerto;
    }

    // ---------- Ensayo completo contra el esquema real ----------

    [Fact]
    public async Task EnsayoCompleto_ConDumpDeEsquemaReal_TerminaLimpio()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var base_ = DirectorioNuevo("ensayo-completo-");
        var trabajo = Path.Combine(base_, "trabajo");
        var dump = Path.Combine(base_, "real.dump");
        var historial = Path.Combine(base_, "backup_20261001_000000000.dump");
        File.WriteAllText(historial, "backup viejo del historial");

        var ejecutor = new EjecutorPgDumpProceso(new ConfiguracionVacia(), NullLogger<EjecutorPgDumpProceso>.Instance);
        var resultado = await ejecutor.EjecutarAsync(_fixture.ConnectionString, dump, CancellationToken.None);
        Assert.True(resultado.Exitoso, $"pg_dump falló: {resultado.MensajeError}");

        var psAntes = Bash("docker ps --format '{{.Names}}' | LC_ALL=C sort");
        Assert.True(psAntes.ExitCode == 0, Diag(psAntes));
        try
        {
            var preparar = Bash($"\"{Script}\" preparar \"{dump}\" --trabajo \"{trabajo}\" " +
                $"--historial \"{historial}\" --sufijo {sufijo} --puerto {PuertoLibre()}");
            Assert.True(preparar.ExitCode == 0, "preparar: " + Diag(preparar));

            var volcar = Bash($"\"{Script}\" volcar --trabajo \"{trabajo}\"");
            Assert.True(volcar.ExitCode == 0, "volcar: " + Diag(volcar));
            Assert.Equal("masked",
                File.ReadAllText(Path.Combine(trabajo, "estado", "stockapp-api.habilitado")).Trim());
            Assert.Equal("RateLimiting__Login__PermitLimit",
                File.ReadAllText(Path.Combine(trabajo, "salida", "overrides-api-env.txt")).Trim());
            var tar = Bash($"tar -tf \"{Path.Combine(trabajo, "salida", "backups-historial.tar")}\"");
            Assert.Contains("backups/backup_20261001_000000000.dump", tar.Stdout);

            var restaurar = Bash($"\"{Script}\" restaurar --trabajo \"{trabajo}\"");
            Assert.True(restaurar.ExitCode == 0, "restaurar: " + Diag(restaurar));

            var limpiar = Bash($"\"{Script}\" limpiar --trabajo \"{trabajo}\"");
            Assert.True(limpiar.ExitCode == 0, "limpiar: " + Diag(limpiar));

            foreach (var r in new[] { preparar, volcar, restaurar, limpiar })
                Assert.Matches(@"\d+ s\b", r.Stdout);

            var contenedores = Bash("docker ps -a --format '{{.Names}}'");
            var volumenes = Bash("docker volume ls --format '{{.Name}}'");
            Assert.True(contenedores.ExitCode == 0 && volumenes.ExitCode == 0, Diag(contenedores) + Diag(volumenes));
            Assert.DoesNotContain(sufijo, contenedores.Stdout);
            Assert.DoesNotContain(sufijo, volumenes.Stdout);
            Assert.Equal(psAntes.Stdout, Bash("docker ps --format '{{.Names}}' | LC_ALL=C sort").Stdout);
        }
        finally
        {
            Bash($"\"{Script}\" limpiar --trabajo \"{trabajo}\"");
        }
    }

    /// <summary>
    /// Reemplazo de <c>new ConfigurationBuilder().Build()</c> sin agregar el paquete concreto
    /// (mismo criterio que RestaurabilidadBackupTests): EjecutorPgDumpProceso resuelve "pg_dump"
    /// por PATH.
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
