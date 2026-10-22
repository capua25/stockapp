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
    /// Corre bash con higiene de entorno (ruling F2, compartida en <see cref="BashLimpio"/>): nada de
    /// STOCKAPP_ENV_LOCAL, VPS_*, ENSAYO_*, STOCKAPP_ENSAYO_* ni ORQ_* de quien corre la suite se
    /// cuela en la corrida.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) Bash(string script) =>
        BashLimpio.Ejecutar(script);

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

    /// <summary>
    /// La seguridad del shim: sin una raíz del ensayo válida no corre NADA (nunca cae al /etc
    /// real). La raíz tiene que ser &lt;trabajo&gt;/vps-viejo, al lado del estado.
    /// </summary>
    [Theory]
    [InlineData("sin-variable")]
    [InlineData("vacia")]
    [InlineData("barra")]
    [InlineData("fuera-del-trabajo")]
    public void ShimSystemctl_SinEnsayoRaiz_Rechaza1(string caso)
    {
        var (estado, _) = VpsViejoSimulado();
        var otra = DirectorioNuevo("ensayo-otra-raiz-");
        var entorno = caso switch
        {
            "sin-variable" => $"export ENSAYO_ESTADO=\"{estado}\"\n",
            "vacia" => $"export ENSAYO_ESTADO=\"{estado}\" ENSAYO_RAIZ=\n",
            "barra" => $"export ENSAYO_ESTADO=\"{estado}\" ENSAYO_RAIZ=/\n",
            _ => $"export ENSAYO_ESTADO=\"{estado}\" ENSAYO_RAIZ=\"{otra}\"\n",
        };

        var r = Bash(entorno + $"\"{Shim("systemctl")}\" is-active stockapp-api");

        Assert.True(r.ExitCode == 1, Diag(r));
        Assert.Equal("", r.Stdout.Trim());
        Assert.Contains("ENSAYO_RAIZ", r.Stderr);
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

    /// <summary>
    /// El directorio de trabajo recibe datos reales (Task 11): nunca dentro del repo (se
    /// commitearía) ni bajo /mnt/ (drvfs de WSL ignora el chmod 700).
    /// </summary>
    [Theory]
    [InlineData("repo")]
    [InlineData("dentro-del-repo")]
    [InlineData("mnt")]
    public void Preparar_TrabajoDentroDelRepoOBajoMnt_Rechaza2(string caso)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var dump = Path.Combine(DirectorioNuevo("ensayo-dump-"), "real.dump");
        File.WriteAllText(dump, "no importa: aborta antes de mirarlo");
        var trabajo = caso switch
        {
            "repo" => RaizRepo(),
            "dentro-del-repo" => Path.Combine(RaizRepo(), "ensayo-trabajo-" + sufijo),
            _ => "/mnt/c/ensayo-trabajo-" + sufijo,
        };

        try
        {
            var r = Bash($"\"{Script}\" preparar \"{dump}\" --trabajo \"{trabajo}\" --sufijo {sufijo} --puerto {PuertoLibre()}");

            Assert.True(r.ExitCode == 2, Diag(r));
            if (caso != "repo") Assert.False(Directory.Exists(trabajo), "se creó el directorio de trabajo");
            Assert.Equal("", Bash(
                $"docker ps -a --format '{{{{.Names}}}}' | grep -e '-{sufijo}$' || true").Stdout.Trim());
        }
        finally
        {
            // Si el guardián faltara, preparar habría creado todo esto: se borra por nombre exacto.
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
            if (caso != "repo" && Directory.Exists(trabajo)) Directory.Delete(trabajo, recursive: true);
        }
    }

    /// <summary>
    /// Un preparar que falla DESPUÉS de crear los contenedores (acá: el dump no es un dump) no deja
    /// nada: el trap borra por nombre exacto lo de este sufijo.
    /// </summary>
    [Fact]
    public void Preparar_FallaAMitad_BorraLoQueCreo()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-pre-"), "trabajo");
        var dump = Path.Combine(DirectorioNuevo("ensayo-dump-"), "real.dump");
        File.WriteAllText(dump, "esto no es un dump de pg_dump");
        try
        {
            var r = Bash($"\"{Script}\" preparar \"{dump}\" --trabajo \"{trabajo}\" --sufijo {sufijo} --puerto {PuertoLibre()}");

            Assert.True(r.ExitCode != 0, Diag(r));
            Assert.Contains("== Dump real en el origen ==", r.Stdout);
            Assert.Equal("", Bash(
                $"docker ps -a --format '{{{{.Names}}}}' | grep -e '-{sufijo}$' || true").Stdout.Trim());
            Assert.Equal("", Bash(
                $"docker volume ls --format '{{{{.Name}}}}' | grep -e '_{sufijo}$' || true").Stdout.Trim());
        }
        finally
        {
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
        }
    }

    /// <summary>
    /// limpiar recuerda SIEMPRE dónde quedan los datos y cuánto ocupan, también cuando sale ≠ 0.
    /// </summary>
    [Theory]
    [InlineData("ps-distinto", 9)]
    [InlineData("sin-ps-antes", 3)]
    public void Limpiar_ConSalidaNoCero_IgualRecuerdaElDirectorioDeDatos(string caso, int exitEsperado)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-limpiar-"), "trabajo");
        Directory.CreateDirectory(trabajo);
        File.WriteAllText(Path.Combine(trabajo, "config"), $"SUFIJO={sufijo}\nPUERTO=5434\n");
        File.WriteAllText(Path.Combine(trabajo, "datos.dump"), new string('x', 50_000));
        if (caso == "ps-distinto")
            File.WriteAllText(Path.Combine(trabajo, "docker-ps-antes.txt"), $"no-existe-{sufijo}\n");
        var tamano = Bash($"du -sh \"{trabajo}\" | cut -f1").Stdout.Trim();

        var r = Bash($"\"{Script}\" limpiar --trabajo \"{trabajo}\"");

        Assert.True(r.ExitCode == exitEsperado, Diag(r));
        var salida = r.Stdout + r.Stderr;
        Assert.Contains(trabajo, salida);
        Assert.Contains($"({tamano})", salida);
        Assert.Contains("borralo a mano", salida);
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

    // ---------- Dos hosts: <trabajo>/hosts/<host>/{estado,raiz,home} ----------

    private const string HostViejo = "vps-viejo.ensayo.invalid";
    private const string HostNuevo = "vps-nuevo.ensayo.invalid";

    private static string DirHost(string trabajo, string host) => Path.Combine(trabajo, "hosts", host);

    /// <summary>
    /// Los dos hosts simulados como los arma preparar-migracion: cada uno con su estado de
    /// systemctl, su raíz (la unit como archivo regular) y su HOME remoto. El viejo active, el
    /// nuevo inactive, para que se note a cuál llegó cada comando.
    /// </summary>
    private string DosHosts()
    {
        var trabajo = DirectorioNuevo("ensayo-hosts-");
        foreach (var (host, activo) in new[] { (HostViejo, "active"), (HostNuevo, "inactive") })
        {
            var dir = DirHost(trabajo, host);
            Directory.CreateDirectory(Path.Combine(dir, "estado"));
            Directory.CreateDirectory(Path.Combine(dir, "home"));
            Directory.CreateDirectory(Path.Combine(dir, "raiz", "etc", "systemd", "system"));
            File.WriteAllText(Path.Combine(dir, "estado", "stockapp-api.activo"), activo + "\n");
            File.WriteAllText(Path.Combine(dir, "estado", "stockapp-api.habilitado"), "enabled\n");
            File.WriteAllText(UnitEtc(Path.Combine(dir, "raiz")), "[Unit]\nDescription=unit simulada\n");
        }
        return trabajo;
    }

    private static string EnvHost(string trabajo, string host) =>
        $"export ENSAYO_ESTADO=\"{Path.Combine(DirHost(trabajo, host), "estado")}\" " +
        $"ENSAYO_RAIZ=\"{Path.Combine(DirHost(trabajo, host), "raiz")}\"\n";

    [Fact]
    public void ShimSsh_EnrutaPorHost()
    {
        var trabajo = DosHosts();

        foreach (var (host, activo) in new[] { (HostViejo, "active"), (HostNuevo, "inactive") })
        {
            // Lo heredado no cuenta: el ssh falso pone el estado, la raíz y el HOME de ESE host.
            var r = Bash($"export ENSAYO_TRABAJO=\"{trabajo}\" ENSAYO_ESTADO=/no/este ENSAYO_RAIZ=/ HOME=/no/este\n" +
                $"\"{Shim("ssh")}\" -p 22 -o BatchMode=yes ensayo@{host} " +
                "'echo \"$HOME|$ENSAYO_ESTADO|$ENSAYO_RAIZ|$PWD\"; systemctl is-active stockapp-api'");

            Assert.True(r.ExitCode == 0 || (activo == "inactive" && r.ExitCode == 3), Diag(r));
            var dir = DirHost(trabajo, host);
            var lineas = r.Stdout.Split('\n');
            Assert.Equal($"{dir}/home|{dir}/estado|{dir}/raiz|{dir}/home", lineas[0]);
            Assert.Equal(activo, lineas[1]);
        }
    }

    /// <summary>
    /// Con dos hosts, el ssh falso solo conoce los dos del arnés: otro .invalid (aunque tenga un
    /// directorio con su forma) o un host real salen con 255 sin correr nada.
    /// </summary>
    [Theory]
    [InlineData("otro.ensayo.invalid")]
    [InlineData("vps.ejemplo.com")]
    public void ShimSsh_OtroHostInvalid_Rechaza255(string host)
    {
        var trabajo = DosHosts();
        var senuelo = DirHost(trabajo, host);
        Directory.CreateDirectory(Path.Combine(senuelo, "estado"));
        Directory.CreateDirectory(Path.Combine(senuelo, "home"));
        Directory.CreateDirectory(Path.Combine(senuelo, "raiz"));
        var marca = Path.Combine(trabajo, "corrio");

        var r = Bash($"export ENSAYO_TRABAJO=\"{trabajo}\"\n" +
            $"\"{Shim("ssh")}\" -p 22 -o BatchMode=yes ensayo@{host} 'touch {marca}; echo corrio'");

        Assert.True(r.ExitCode == 255, Diag(r));
        Assert.Contains("host no permitido", r.Stderr);
        Assert.DoesNotContain("corrio", r.Stdout);
        Assert.False(File.Exists(marca), $"el comando remoto corrió contra '{host}'");
    }

    [Fact]
    public void ShimScp_CopiaAlHomeDelHost()
    {
        var trabajo = DosHosts();
        var local = DirectorioNuevo("ensayo-scp-local-");
        var homeOperador = DirectorioNuevo("ensayo-scp-home-");
        var a = Path.Combine(local, "volcado.dump");
        var b = Path.Combine(local, "volcado.manifiesto");
        File.WriteAllText(a, "datos");
        File.WriteAllText(b, "manifiesto");
        var homeNuevo = Path.Combine(DirHost(trabajo, HostNuevo), "home");
        Directory.CreateDirectory(Path.Combine(homeNuevo, "stockapp-deploy", "migracion", "corrida"));
        var scp = $"\"{Shim("scp")}\"";

        var r = BashLimpio.Ejecutar($"export ENSAYO_TRABAJO=\"{trabajo}\"\n" +
            $"{scp} -P 22 -o BatchMode=yes -- \"{a}\" \"{b}\" ensayo@{HostNuevo}:stockapp-deploy/migracion/corrida/ || exit 11\n" +
            $"{scp} -P 22 -o BatchMode=yes -- \"{a}\" ensayo@{HostNuevo}:~/en-el-home.dump", homeOperador);

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("datos", File.ReadAllText(Path.Combine(homeNuevo, "stockapp-deploy", "migracion", "corrida", "volcado.dump")));
        Assert.Equal("manifiesto", File.ReadAllText(Path.Combine(homeNuevo, "stockapp-deploy", "migracion", "corrida", "volcado.manifiesto")));
        Assert.Equal("datos", File.ReadAllText(Path.Combine(homeNuevo, "en-el-home.dump")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(DirHost(trabajo, HostViejo), "home")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(homeOperador));

        // Fuera de los dos hosts del arnés no copia nada.
        var otro = BashLimpio.Ejecutar($"export ENSAYO_TRABAJO=\"{trabajo}\"\n" +
            $"{scp} -P 22 -- \"{a}\" ensayo@otro.ensayo.invalid:x.dump", homeOperador);
        Assert.True(otro.ExitCode == 255, Diag(otro));
        // Una ruta remota absoluta fuera del host simulado tampoco (nunca el disco real).
        var fuera = BashLimpio.Ejecutar($"export ENSAYO_TRABAJO=\"{trabajo}\"\n" +
            $"{scp} -P 22 -- \"{a}\" ensayo@{HostNuevo}:{homeOperador}/x.dump", homeOperador);
        Assert.True(fuera.ExitCode != 0, Diag(fuera));
        Assert.Empty(Directory.EnumerateFileSystemEntries(homeOperador));
    }

    /// <summary>
    /// Fiel a systemd 255 (verificado en esta máquina con una unit inexistente): start de una unit
    /// sin archivo falla con 5 y "Unit … not found.". Es lo que hace que un --revertir que olvida
    /// devolver el .congelado a su lugar falle en el ensayo igual que en el VPS real.
    /// </summary>
    [Fact]
    public void ShimSystemctl_StartSinArchivoDeUnit_Falla5()
    {
        var trabajo = DosHosts();
        var raiz = Path.Combine(DirHost(trabajo, HostNuevo), "raiz");
        File.Move(UnitEtc(raiz), UnitEtc(raiz) + ".congelado");

        var r = Bash(EnvHost(trabajo, HostNuevo) + $"\"{Shim("systemctl")}\" start stockapp-api");

        Assert.True(r.ExitCode == 5, Diag(r));
        Assert.Contains("Unit stockapp-api.service not found.", r.Stderr);
        Assert.Equal("inactive",
            File.ReadAllText(Path.Combine(DirHost(trabajo, HostNuevo), "estado", "stockapp-api.activo")).Trim());
    }

    [Fact]
    public void ShimSystemctl_RaizEnHosts_Acepta()
    {
        var trabajo = DosHosts();

        var r = Bash(EnvHost(trabajo, HostViejo) + $"\"{Shim("systemctl")}\" is-active stockapp-api");

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal("active", r.Stdout.Trim());
    }

    [Theory]
    [InlineData("barra")]
    [InlineData("raiz-del-otro-host")]
    public void ShimSystemctl_RaizBarra_Rechaza1(string caso)
    {
        var trabajo = DosHosts();
        var estado = Path.Combine(DirHost(trabajo, HostNuevo), "estado");
        var raiz = caso == "barra" ? "/" : Path.Combine(DirHost(trabajo, HostViejo), "raiz");

        var r = Bash($"export ENSAYO_ESTADO=\"{estado}\" ENSAYO_RAIZ=\"{raiz}\"\n" +
            $"\"{Shim("systemctl")}\" is-active stockapp-api");

        Assert.True(r.ExitCode == 1, Diag(r));
        Assert.Equal("", r.Stdout.Trim());
        Assert.Contains("ENSAYO_RAIZ", r.Stderr);
    }

    private static bool Vivo(int pid) => Bash($"ps -p {pid} -o pid= >/dev/null").ExitCode == 0;

    /// <summary>
    /// Con &lt;estado&gt;/api.lanzar, start lanza el ejecutable (acá uno trivial del test, no la
    /// API) con el entorno de la unit y del api.env simulados, desacoplado y sin heredar nada de
    /// quien llama; stop lo mata por el PID guardado y verifica con ps; un PID muerto es failed.
    /// </summary>
    [Fact]
    public async Task ShimSystemctl_StartLanzaYStopMataPorPid()
    {
        var trabajo = DosHosts();
        var dir = DirHost(trabajo, HostNuevo);
        var raiz = Path.Combine(dir, "raiz");
        var estado = Path.Combine(dir, "estado");
        var entornoVisto = Path.Combine(trabajo, "entorno-del-proceso.txt");
        var ejecutable = Path.Combine(trabajo, "api-trivial");
        BashLimpio.EscribirEjecutable(ejecutable,
            $"#!/usr/bin/env bash\nenv > \"{entornoVisto}\"\nexec tail -f /dev/null\n");
        File.WriteAllText(UnitEtc(raiz),
            "[Service]\nEnvironment=HOME=/var/lib/stockapp\nEnvironment=ASPNETCORE_URLS=http://127.0.0.1:59999\n");
        Directory.CreateDirectory(Path.Combine(raiz, "etc", "stockapp-api"));
        File.WriteAllText(Path.Combine(raiz, "etc", "stockapp-api", "api.env"), "# comentario\nENSAYO_PRUEBA=del-api-env\n");
        File.WriteAllText(Path.Combine(estado, "api.lanzar"), ejecutable + "\n");
        var systemctl = $"\"{Shim("systemctl")}\"";
        var pids = new List<int>();
        try
        {
            var start = Bash(EnvHost(trabajo, HostNuevo) + $"export SECRETO_DEL_OPERADOR=no-se-hereda\n{systemctl} start stockapp-api");
            Assert.True(start.ExitCode == 0, Diag(start));
            var pid = int.Parse(File.ReadAllText(Path.Combine(estado, "api.pid")).Trim());
            pids.Add(pid);
            Assert.True(await EsperaMonotonica.HastaAsync(() => File.Exists(entornoVisto) && File.ReadAllText(entornoVisto).Contains("ENSAYO_PRUEBA"),
                TimeSpan.FromSeconds(20)), "el ejecutable no arrancó");
            Assert.True(Vivo(pid), "el proceso lanzado no está vivo");
            var entorno = File.ReadAllText(entornoVisto).Split('\n');
            Assert.Contains("ENSAYO_PRUEBA=del-api-env", entorno);
            Assert.Contains($"HOME={raiz}/var/lib/stockapp", entorno);
            Assert.Contains("ASPNETCORE_URLS=http://127.0.0.1:59999", entorno);
            Assert.Contains($"DOTNET_CLI_HOME={dir}/home", entorno);
            Assert.DoesNotContain(entorno, l => l.StartsWith("SECRETO_DEL_OPERADOR="));
            Assert.Equal("active", Bash(EnvHost(trabajo, HostNuevo) + $"{systemctl} is-active stockapp-api").Stdout.Trim());

            var stop = Bash(EnvHost(trabajo, HostNuevo) + $"{systemctl} stop stockapp-api");
            Assert.True(stop.ExitCode == 0, Diag(stop));
            Assert.False(Vivo(pid), "stop no mató el proceso");
            var inactiva = Bash(EnvHost(trabajo, HostNuevo) + $"{systemctl} is-active stockapp-api");
            Assert.True(inactiva.ExitCode == 3, Diag(inactiva));
            Assert.Equal("inactive", inactiva.Stdout.Trim());

            // Un PID que murió solo (como una API que se cae) es failed, no active.
            File.Delete(entornoVisto);
            var otra = Bash(EnvHost(trabajo, HostNuevo) + $"{systemctl} start stockapp-api");
            Assert.True(otra.ExitCode == 0, Diag(otra));
            var pid2 = int.Parse(File.ReadAllText(Path.Combine(estado, "api.pid")).Trim());
            pids.Add(pid2);
            Assert.True(await EsperaMonotonica.HastaAsync(() => File.Exists(entornoVisto), TimeSpan.FromSeconds(20)));
            Bash($"kill -KILL -- -{pid2}");
            Assert.True(await EsperaMonotonica.HastaAsync(() => !Vivo(pid2), TimeSpan.FromSeconds(20)));
            var caida = Bash(EnvHost(trabajo, HostNuevo) + $"{systemctl} is-active stockapp-api");
            Assert.True(caida.ExitCode == 3, Diag(caida));
            Assert.Equal("failed", caida.Stdout.Trim());
        }
        finally
        {
            foreach (var p in pids)
                if (Vivo(p)) Bash($"kill -KILL -- -{p}");
        }
    }

    // ---------- ensayo-local.sh: preparar-migracion, migrar y limpiar ----------

    /// <summary>Lo que corre en la máquina: contenedores (todos) y volúmenes, ordenados.</summary>
    private static string[] Lineas(string comando)
    {
        var r = Bash(comando + " | LC_ALL=C sort");
        Assert.True(r.ExitCode == 0, Diag(r));
        return r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] TodosLosContenedores() => Lineas("docker ps -a --format '{{.Names}}'");

    private static string EstadoDe(string contenedor) =>
        Bash($"docker inspect -f '{{{{.Id}}}} {{{{.State.Status}}}} {{{{.State.StartedAt}}}}' {contenedor} 2>/dev/null || echo ausente").Stdout.Trim();

    private static int? PidDe(string trabajo, string host)
    {
        var archivo = Path.Combine(DirHost(trabajo, host), "estado", "api.pid");
        return File.Exists(archivo) ? int.Parse(File.ReadAllText(archivo).Trim()) : null;
    }

    /// <summary>
    /// La caché de NuGet de quien corre la suite: con el HOME del test, dotnet publish volvería a
    /// bajar todos los paquetes (y los escribiría en ese HOME, que el test exige vacío).
    /// </summary>
    private static string CacheNuget =>
        Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } c
            ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    private (int ExitCode, string Stdout, string Stderr) PrepararMigracion(string trabajo, string sufijo, string home) =>
        BashLimpio.Ejecutar($"export NUGET_PACKAGES=\"{CacheNuget}\"\n" +
            $"\"{Script}\" preparar-migracion --trabajo \"{trabajo}\" --sufijo {sufijo} " +
            $"--puerto-viejo {PuertoLibre()} --puerto-nuevo {PuertoLibre()}", home);

    /// <summary>
    /// preparar-migracion crea SOLO los dos contenedores de su sufijo (nunca toca el de
    /// desarrollo), deja los dos hosts con machine-id distintos y las dos APIs reales atendiendo,
    /// y los .env del operador apuntando a los hosts .invalid por http. Nada fuera del trabajo.
    /// </summary>
    [Fact]
    public void PrepararMigracion_NombresSoloDeEnsayo()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-mig-"), "trabajo");
        var home = DirectorioNuevo("ensayo-mig-home-");
        var antes = TodosLosContenedores();
        var desarrollo = EstadoDe("stockapp-pg");
        try
        {
            var r = PrepararMigracion(trabajo, sufijo, home);
            Assert.True(r.ExitCode == 0, "preparar-migracion: " + Diag(r));

            var nuevos = TodosLosContenedores().Except(antes).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { $"stockapp-ensayo-origen-pg-{sufijo}", $"stockapp-ensayo-pg-{sufijo}" }, nuevos);
            Assert.Equal(desarrollo, EstadoDe("stockapp-pg"));
            Assert.Empty(Directory.EnumerateFileSystemEntries(home));

            var ids = new[] { HostViejo, HostNuevo }
                .Select(h => File.ReadAllText(Path.Combine(DirHost(trabajo, h), "raiz", "etc", "machine-id")).Trim()).ToArray();
            Assert.All(ids, id => Assert.Matches("^[0-9a-f]{32}$", id));
            Assert.NotEqual(ids[0], ids[1]);

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(trabajo, "admin-password")));
            foreach (var (host, archivo) in new[] { (HostViejo, "env-vps-viejo-ensayo"), (HostNuevo, "env-vps-nuevo-ensayo") })
            {
                var env = File.ReadAllLines(Path.Combine(trabajo, archivo));
                Assert.Contains($"VPS_HOST={host}", env);
                Assert.Contains("API_SCHEME=http", env);
                Assert.Contains("API_HOSTNAME=127.0.0.1", env);
                var puerto = env.Single(l => l.StartsWith("API_PORT=")).Split('=')[1];
                var pid = PidDe(trabajo, host);
                Assert.True(pid is not null && Vivo(pid.Value), $"la API de {host} no está viva");
                var raiz = Bash($"curl -fsS -o /dev/null -w '%{{http_code}}' http://127.0.0.1:{puerto}/");
                Assert.True(raiz.ExitCode == 0, $"GET / de {host}: " + Diag(raiz));
            }
            var puertoNuevo = File.ReadAllLines(Path.Combine(trabajo, "env-vps-nuevo-ensayo"))
                .Single(l => l.StartsWith("API_PORT=")).Split('=')[1];
            var feed = Bash($"curl -fsS http://127.0.0.1:{puertoNuevo}/updates/releases.win.json | jq -r '.Assets[0].Type'");
            Assert.True(feed.ExitCode == 0, Diag(feed));
            Assert.Equal("Full", feed.Stdout.Trim());
            Assert.True(File.Exists(Path.Combine(DirHost(trabajo, HostNuevo), "home", "stockapp-deploy", ".env")));
            Assert.True(Directory.Exists(Path.Combine(DirHost(trabajo, HostNuevo), "home", "stockapp-deploy", "migracion")));
        }
        finally
        {
            BashLimpio.Ejecutar($"\"{Script}\" limpiar --trabajo \"{trabajo}\"", home);
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
        }
    }

    /// <summary>
    /// limpiar mata las dos APIs por su PID exacto (y lo verifica con ps) y borra los dos
    /// contenedores y volúmenes por nombre exacto: no queda nada vivo del ensayo y docker ps es el
    /// de antes.
    /// </summary>
    [Fact]
    public void Limpiar_NoQuedanProcesosNiContenedores()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-mig-"), "trabajo");
        var home = DirectorioNuevo("ensayo-mig-home-");
        var psAntes = Lineas("docker ps --format '{{.Names}}'");
        var pids = new List<int>();
        try
        {
            var r = PrepararMigracion(trabajo, sufijo, home);
            Assert.True(r.ExitCode == 0, "preparar-migracion: " + Diag(r));
            foreach (var h in new[] { HostViejo, HostNuevo })
                pids.Add(PidDe(trabajo, h) ?? throw new InvalidOperationException($"{h} sin api.pid"));
            Assert.All(pids, p => Assert.True(Vivo(p)));

            var limpiar = BashLimpio.Ejecutar($"\"{Script}\" limpiar --trabajo \"{trabajo}\"", home);

            Assert.True(limpiar.ExitCode == 0, "limpiar: " + Diag(limpiar));
            Assert.All(pids, p => Assert.False(Vivo(p), $"la API con PID {p} sigue viva"));
            Assert.DoesNotContain(TodosLosContenedores(), n => n.EndsWith("-" + sufijo));
            Assert.DoesNotContain(Lineas("docker volume ls --format '{{.Name}}'"), n => n.EndsWith("_" + sufijo));
            Assert.Equal(psAntes, Lineas("docker ps --format '{{.Names}}'"));
        }
        finally
        {
            foreach (var p in pids)
                if (Vivo(p)) Bash($"kill -KILL -- -{p}");
            BashLimpio.Ejecutar($"\"{Script}\" limpiar --trabajo \"{trabajo}\"", home);
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
        }
    }

    /// <summary>
    /// El directorio de trabajo nunca es el repo, nada bajo /mnt/, ni el HOME entero (ni uno de
    /// sus padres): exit 2 sin crear nada.
    /// </summary>
    [Theory]
    [InlineData("dentro-del-repo")]
    [InlineData("mnt")]
    [InlineData("home")]
    [InlineData("padre-del-home")]
    public void PrepararMigracion_TrabajoPeligroso_Rechaza2(string caso)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var padre = DirectorioNuevo("ensayo-mig-padre-");
        var home = Path.Combine(padre, "home");
        Directory.CreateDirectory(home);
        // Un HOME de verdad nunca está vacío. Además, si el guardián faltara, preparar-migracion se
        // detiene en "no está vacío" en vez de levantar contenedores y APIs.
        var perfil = Path.Combine(home, ".profile");
        File.WriteAllText(perfil, "# perfil\n");
        var trabajo = caso switch
        {
            "dentro-del-repo" => Path.Combine(RaizRepo(), "ensayo-trabajo-" + sufijo),
            "mnt" => "/mnt/c/ensayo-trabajo-" + sufijo,
            "home" => home,
            _ => padre,
        };
        try
        {
            var r = BashLimpio.Ejecutar($"\"{Script}\" preparar-migracion --trabajo \"{trabajo}\" --sufijo {sufijo}", home);

            Assert.True(r.ExitCode == 2, Diag(r));
            if (caso is "dentro-del-repo" or "mnt") Assert.False(Directory.Exists(trabajo), "se creó el directorio de trabajo");
            Assert.Equal(new[] { perfil }, Directory.EnumerateFileSystemEntries(home).ToArray());
            Assert.DoesNotContain(TodosLosContenedores(), n => n.EndsWith("-" + sufijo));
        }
        finally
        {
            // Si el guardián faltara y algo arrancara igual, ninguna API queda viva.
            foreach (var h in new[] { HostViejo, HostNuevo })
                if (Directory.Exists(trabajo) && PidDe(trabajo, h) is { } p && Vivo(p)) Bash($"kill -KILL -- -{p}");
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
            if (caso is "dentro-del-repo" or "mnt" && Directory.Exists(trabajo)) Directory.Delete(trabajo, recursive: true);
        }
    }

    /// <summary>
    /// migrar le pasa a migrar-vps.sh los contenedores y las raíces del arnés (R13, R11): todo
    /// docker exec va al contenedor del ensayo por nombre, nunca al stockapp-pg de desarrollo, y el
    /// machine-id sale de la raíz de cada host. Acá docker es un envoltorio del test que registra
    /// y aborta ante stockapp-pg (el arnés no tiene shim de docker); la corrida se corta en la
    /// credencial porque no hay APIs, y para entonces ya consultó las dos bases.
    /// </summary>
    [Fact]
    public void Migrar_ReenviaContenedoresYRaices_NuncaStockappPg()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = DosHosts();
        var home = DirectorioNuevo("ensayo-mig-home-");
        var bin = DirectorioNuevo("ensayo-mig-bin-");
        var rastro = Path.Combine(bin, "docker.rastro");
        var (puertoViejo, puertoNuevo) = (PuertoLibre(), PuertoLibre());
        File.WriteAllText(Path.Combine(trabajo, "config"),
            $"TIPO=migracion\nSUFIJO={sufijo}\nPUERTO=5434\nPUERTO_VIEJO={puertoViejo}\nPUERTO_NUEVO={puertoNuevo}\n");
        Directory.CreateDirectory(Path.Combine(trabajo, "operador"));
        var n = 0;
        foreach (var (host, archivo, puerto) in new[] { (HostViejo, "env-vps-viejo-ensayo", puertoViejo), (HostNuevo, "env-vps-nuevo-ensayo", puertoNuevo) })
        {
            File.WriteAllText(Path.Combine(DirHost(trabajo, host), "raiz", "etc", "machine-id"),
                (++n).ToString().PadLeft(32, 'a') + "\n");
            File.WriteAllText(Path.Combine(trabajo, archivo),
                $"VPS_HOST={host}\nVPS_USER=ensayo\nVPS_SSH_PORT=22\nAPI_SCHEME=http\nAPI_HOSTNAME=127.0.0.1\n" +
                $"API_PORT={puerto}\nPOSTGRES_USER=stockapp\nPOSTGRES_DB=stockapp\nVPS_ADMIN_USER=admin\n");
        }
        Directory.CreateDirectory(Path.Combine(DirHost(trabajo, HostNuevo), "home", "stockapp-deploy", "migracion"));
        File.WriteAllText(Path.Combine(DirHost(trabajo, HostNuevo), "home", "stockapp-deploy", ".env"), "POSTGRES_USER=stockapp\n");
        BashLimpio.EscribirEjecutable(Path.Combine(bin, "docker"),
            "#!/usr/bin/env bash\n" +
            $"printf '%s\\n' \"$*\" >> \"{rastro}\"\n" +
            "for a in \"$@\"; do [[ \"$a\" != stockapp-pg ]] || { echo TOCO-STOCKAPP-PG >> \"" + rastro + "\"; exit 1; }; done\n" +
            "case \"$1 $*\" in\n" +
            // Como en esta máquina: el de desarrollo también está corriendo.
            $"  ps*) printf 'stockapp-pg\\nstockapp-ensayo-origen-pg-{sufijo}\\n' ;;\n" +
            "  *' df '*) printf 'Filesystem 1024-blocks Used Available Capacity Mounted\\noverlay 100000000 1 99999999 1%% /data\\n' ;;\n" +
            "  *psql*) cat >/dev/null; echo 12345 ;;\n" +
            "  *) exit 1 ;;\n" +
            "esac\n");

        var r = BashLimpio.Ejecutar($"export PATH=\"{bin}:$PATH\"\n" +
            $"\"{Script}\" migrar --trabajo \"{trabajo}\" -- --ensayo", home);

        var docker = File.Exists(rastro) ? File.ReadAllText(rastro) : "";
        Assert.DoesNotContain("TOCO-STOCKAPP-PG", docker);
        Assert.Contains($"exec -i stockapp-ensayo-origen-pg-{sufijo} psql", docker);
        Assert.Contains($"exec -i stockapp-ensayo-pg-{sufijo} psql", docker);
        Assert.DoesNotContain("solo existen para el ensayo local", r.Stderr);
        Assert.True(r.ExitCode != 0, Diag(r));
        Assert.True(Directory.Exists(Path.Combine(trabajo, "operador", "stockapp-backups-vps", "corridas")),
            "las corridas no quedaron en el HOME del operador simulado: " + Diag(r));
        Assert.Empty(Directory.EnumerateFileSystemEntries(home));
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
