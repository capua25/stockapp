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

        var psAntes = ContenedoresCorriendo();
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
            Assert.Equal(psAntes, ContenedoresCorriendo());
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

    /// <summary>
    /// Los contenedores corriendo, sin los de Testcontainers (label org.testcontainers=true, R55): el
    /// mismo criterio que la comparación de limpiar. El ryuk y el fixture de otra corrida de la suite
    /// aparecen y desaparecen solos; todo otro contenedor cuenta.
    /// </summary>
    private static string[] ContenedoresCorriendo() =>
        Lineas("docker ps --format '{{.Names}}\\t{{.Label \"org.testcontainers\"}}' | awk -F'\\t' '$2 != \"true\" { print $1 }'");

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
            MatarApisDeRespaldo(trabajo);
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
        var psAntes = ContenedoresCorriendo();
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
            Assert.Equal(psAntes, ContenedoresCorriendo());
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
    /// Respaldo de limpiar: si quedó alguna API del ensayo viva (limpiar falló o no llegó a correr),
    /// se mata su grupo por el PID exacto de su api.pid.
    /// </summary>
    private static void MatarApisDeRespaldo(string trabajo)
    {
        foreach (var h in new[] { HostViejo, HostNuevo })
            if (Directory.Exists(trabajo) && PidDe(trabajo, h) is { } p && Vivo(p)) Bash($"kill -KILL -- -{p}");
    }

    private static bool PuertoAtiende(int puerto) =>
        Bash($"curl -s -o /dev/null --max-time 5 http://127.0.0.1:{puerto}/").ExitCode == 0;

    /// <summary>
    /// Ctrl-C (INT) o un TERM en pleno preparar-migracion, con las dos APIs ya lanzadas: el trap
    /// las detiene y borra los contenedores, igual que un fallo. Sin los traps de señal, el trap
    /// EXIT ve $?=0 y deja todo vivo (las APIs corren con setsid: la terminal no las alcanza).
    /// Sincronización sin esperas: un curl del test en el PATH se detiene en la consulta del feed
    /// del nuevo (cuando las dos APIs ya atienden) y avisa por un fifo; el test manda la señal al
    /// PID exacto y recién después lo deja seguir. Las lecturas y escrituras de los fifos van con
    /// timeout acotado.
    /// </summary>
    [Theory]
    [InlineData("INT", 130)]
    [InlineData("TERM", 143)]
    public void PrepararMigracion_InterrumpidoPorSenal_NoDejaApisNiContenedores(string senal, int exitEsperado)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-mig-"), "trabajo");
        var home = DirectorioNuevo("ensayo-mig-home-");
        var bin = DirectorioNuevo("ensayo-mig-bin-");
        var listo = Path.Combine(bin, "listo.fifo");
        var seguir = Path.Combine(bin, "seguir.fifo");
        var curlReal = Bash("command -v curl").Stdout.Trim();
        Assert.True(Bash($"mkfifo \"{listo}\" \"{seguir}\"").ExitCode == 0);
        BashLimpio.EscribirEjecutable(Path.Combine(bin, "curl"),
            "#!/usr/bin/env bash\n" +
            "for a in \"$@\"; do\n" +
            "  if [[ \"$a\" == */updates/releases.win.json ]]; then\n" +
            $"    timeout 300 bash -c 'echo listo > \"$1\"' _ \"{listo}\"\n" +
            $"    timeout 300 bash -c 'read -r _ < \"$1\"' _ \"{seguir}\"\n" +
            "  fi\n" +
            "done\n" +
            $"exec \"{curlReal}\" \"$@\"\n");
        var (puertoViejo, puertoNuevo) = (PuertoLibre(), PuertoLibre());
        using var proceso = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        proceso.StartInfo.ArgumentList.Add("-c");
        // exec: el PID del proceso ES el del script, así la señal le llega a él y a nadie más.
        proceso.StartInfo.ArgumentList.Add(BashLimpio.Limpio(
            $"export NUGET_PACKAGES=\"{CacheNuget}\" PATH=\"{bin}:$PATH\"\n" +
            $"exec \"{Script}\" preparar-migracion --trabajo \"{trabajo}\" --sufijo {sufijo} " +
            $"--puerto-viejo {puertoViejo} --puerto-nuevo {puertoNuevo}", home));
        var pids = new List<int>();
        try
        {
            proceso.Start();
            var salida = proceso.StandardOutput.ReadToEndAsync();
            var errores = proceso.StandardError.ReadToEndAsync();

            var aviso = Bash($"timeout 300 cat \"{listo}\"");
            Assert.True(aviso.ExitCode == 0 && aviso.Stdout.Trim() == "listo", "el script no llegó al feed del nuevo: " + Diag(aviso));
            foreach (var h in new[] { HostViejo, HostNuevo })
                pids.Add(PidDe(trabajo, h) ?? throw new InvalidOperationException($"{h} sin api.pid"));
            Assert.All(pids, p => Assert.True(Vivo(p)));

            Assert.True(Bash($"kill -{senal} {proceso.Id}").ExitCode == 0);
            Assert.True(Bash($"timeout 30 bash -c 'echo seguir > \"$1\"' _ \"{seguir}\"").ExitCode == 0);
            Assert.True(proceso.WaitForExit(180_000), "preparar-migracion no terminó después de la señal");
            Task.WaitAll(salida, errores);
            var r = (proceso.ExitCode, salida.Result, errores.Result);

            Assert.True(r.ExitCode == exitEsperado, Diag(r));
            Assert.All(pids, p => Assert.False(Vivo(p), $"la API con PID {p} sigue viva: " + Diag(r)));
            Assert.False(PuertoAtiende(puertoViejo), $"algo sigue atendiendo en {puertoViejo}");
            Assert.False(PuertoAtiende(puertoNuevo), $"algo sigue atendiendo en {puertoNuevo}");
            Assert.DoesNotContain(TodosLosContenedores(), n => n.EndsWith("-" + sufijo));
            Assert.DoesNotContain(Lineas("docker volume ls --format '{{.Name}}'"), n => n.EndsWith("_" + sufijo));
        }
        finally
        {
            // Destrabar el curl del test si quedó esperando, y nada vivo pase lo que pase.
            Bash($"timeout 2 cat \"{listo}\" >/dev/null 2>&1; timeout 2 bash -c 'echo x > \"$1\"' _ \"{seguir}\" 2>/dev/null; true");
            if (!proceso.HasExited) Bash($"kill -KILL {proceso.Id}");
            foreach (var p in pids)
                if (Vivo(p)) Bash($"kill -KILL -- -{p}");
            MatarApisDeRespaldo(trabajo);
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
        }
    }

    /// <summary>
    /// dotnet publish corre con HOME y TMPDIR dentro del trabajo, pero el lock de NuGet tiene que
    /// seguir en su lugar real (R54): la caché de paquetes es la compartida, y un restore
    /// concurrente (un IDE, otro worktree) se excluye por ese lock. Un dotnet del test registra el
    /// entorno y falla, así preparar-migracion aborta antes de crear contenedores.
    /// </summary>
    [Fact]
    public void PrepararMigracion_PublishConElLockDeNugetReal()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-mig-"), "trabajo");
        var home = DirectorioNuevo("ensayo-mig-home-");
        var bin = DirectorioNuevo("ensayo-mig-bin-");
        var visto = Path.Combine(bin, "entorno-de-dotnet.txt");
        BashLimpio.EscribirEjecutable(Path.Combine(bin, "dotnet"),
            $"#!/usr/bin/env bash\nenv > \"{visto}\"\nexit 1\n");
        var usuario = Bash("id -un").Stdout.Trim();
        try
        {
            var r = BashLimpio.Ejecutar($"unset NUGET_SCRATCH; export TMPDIR=/tmp NUGET_PACKAGES=\"{CacheNuget}\" PATH=\"{bin}:$PATH\"\n" +
                $"\"{Script}\" preparar-migracion --trabajo \"{trabajo}\" --sufijo {sufijo} " +
                $"--puerto-viejo {PuertoLibre()} --puerto-nuevo {PuertoLibre()}", home);

            Assert.True(r.ExitCode == 8, Diag(r));
            var entorno = File.ReadAllLines(visto);
            Assert.Contains($"NUGET_SCRATCH=/tmp/NuGetScratch{usuario}", entorno);
            Assert.Contains($"NUGET_PACKAGES={CacheNuget}", entorno);
            Assert.Contains($"HOME={trabajo}/dotnet-cli", entorno);
            Assert.Contains($"TMPDIR={trabajo}/tmp", entorno);
            Assert.DoesNotContain(TodosLosContenedores(), n => n.EndsWith("-" + sufijo));
        }
        finally
        {
            Bash($"docker rm -f -v stockapp-ensayo-pg-{sufijo} stockapp-ensayo-origen-pg-{sufijo} 2>/dev/null; " +
                 $"docker volume rm stockapp_ensayo_origen_pgdata_{sufijo} stockapp_ensayo_pgdata_{sufijo} 2>/dev/null; true");
        }
    }

    /// <summary>
    /// El journal de stockapp-api de cada host simulado es el api.log de su API (R53): V2 y el
    /// diagnóstico de migrar-vps.sh, con los comandos tal cual los manda, leen ESE log y no el
    /// journal real de esta máquina ("-- No entries --"). --since filtra por la hora de cada línea
    /// (la plantilla de Serilog de la API empieza con ella).
    /// </summary>
    [Fact]
    public void ShimJournalctl_LeeElApiLogDelHost()
    {
        var trabajo = DosHosts();
        File.WriteAllText(Path.Combine(DirHost(trabajo, HostNuevo), "estado", "api.log"),
            "2026-10-22 01:40:00.000 +00:00 [WRN] antes del arranque\n" +
            "2026-10-22 01:46:41.123 -03:00 [ERR] Falló algo del nuevo\n" +
            "System.Exception: el detalle de la excepción\n" +
            "2026-10-22 04:47:00.000 +00:00 [WRN] una advertencia después\n");
        File.WriteAllText(Path.Combine(DirHost(trabajo, HostViejo), "estado", "api.log"),
            "2026-10-22 05:00:00.000 +00:00 [ERR] del VIEJO\n");
        var ssh = $"export ENSAYO_TRABAJO=\"{trabajo}\"\n\"{Shim("ssh")}\" -p 22 -o BatchMode=yes ensayo@{HostNuevo} ";

        var desde = Bash(ssh + "\"sudo -n journalctl -u stockapp-api --since '2026-10-22 04:46:40 UTC' --no-pager -o cat\"");
        var ultimas = Bash(ssh + "\"sudo -n journalctl -u stockapp-api -n 2 --no-pager\"");
        var otra = Bash(ssh + "\"sudo -n journalctl -u otra-app --no-pager\"");

        Assert.True(desde.ExitCode == 0, Diag(desde));
        Assert.Equal(
            "2026-10-22 01:46:41.123 -03:00 [ERR] Falló algo del nuevo\n" +
            "System.Exception: el detalle de la excepción\n" +
            "2026-10-22 04:47:00.000 +00:00 [WRN] una advertencia después", desde.Stdout);
        Assert.DoesNotContain("No entries", desde.Stdout);
        Assert.True(ultimas.ExitCode == 0, Diag(ultimas));
        Assert.Equal("System.Exception: el detalle de la excepción\n2026-10-22 04:47:00.000 +00:00 [WRN] una advertencia después", ultimas.Stdout);
        Assert.True(otra.ExitCode != 0, Diag(otra));
        Assert.Equal("", otra.Stdout);
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
    /// R55: la comparación de limpiar no cuenta los contenedores de Testcontainers (label
    /// org.testcontainers=true): el ryuk y el fixture de OTRA corrida de la suite aparecen y
    /// desaparecen solos, y el fixture tiene un nombre aleatorio. Cualquier otro contenedor que
    /// aparezca o desaparezca entre preparar y limpiar sigue dando 9. Un dotnet del test hace que
    /// preparar-migracion aborte justo después de guardar la lista (sin APIs ni Postgres).
    /// </summary>
    [Theory]
    [InlineData(true, "desaparece", 0)]
    [InlineData(true, "aparece", 0)]
    [InlineData(false, "desaparece", 9)]
    [InlineData(false, "aparece", 9)]
    public void Limpiar_SoloLosContenedoresDeTestcontainersQuedanFueraDeLaComparacion(bool conLabel, string cambio, int exitEsperado)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var trabajo = Path.Combine(DirectorioNuevo("ensayo-r55-"), "trabajo");
        var home = DirectorioNuevo("ensayo-r55-home-");
        var bin = DirectorioNuevo("ensayo-r55-bin-");
        BashLimpio.EscribirEjecutable(Path.Combine(bin, "dotnet"), "#!/usr/bin/env bash\nexit 1\n");
        var ajeno = $"stockapp-ensayo-ajeno-{sufijo}";
        var crear = $"docker run -d --name {ajeno} {(conLabel ? "--label org.testcontainers=true " : "")}alpine sleep 300 >/dev/null";
        try
        {
            if (cambio == "desaparece") Assert.True(Bash(crear).ExitCode == 0, "no se pudo crear " + ajeno);
            var preparar = BashLimpio.Ejecutar($"export PATH=\"{bin}:$PATH\"\n" +
                $"\"{Script}\" preparar-migracion --trabajo \"{trabajo}\" --sufijo {sufijo} " +
                $"--puerto-viejo {PuertoLibre()} --puerto-nuevo {PuertoLibre()}", home);
            Assert.True(preparar.ExitCode == 8, "preparar-migracion (dotnet del test que falla): " + Diag(preparar));
            if (cambio == "desaparece") Assert.True(Bash($"docker rm -f {ajeno} >/dev/null").ExitCode == 0);
            else Assert.True(Bash(crear).ExitCode == 0, "no se pudo crear " + ajeno);

            var r = BashLimpio.Ejecutar($"\"{Script}\" limpiar --trabajo \"{trabajo}\"", home);

            Assert.True(r.ExitCode == exitEsperado, Diag(r));
            if (exitEsperado == 9) Assert.Contains(ajeno, r.Stderr);
        }
        finally
        {
            Bash($"docker rm -f {ajeno} 2>/dev/null; true");
        }
    }

    /// <summary>
    /// Con dos hosts, el sudo falso deja un rastro ordenado de lo que corre como root en cada host
    /// simulado (&lt;trabajo&gt;/sudo.rastro, "&lt;host&gt; &lt;comando&gt;"): es lo que permite
    /// afirmar el ORDEN de la vuelta atrás en el viejo y que el nuevo se detuvo antes. Sin
    /// ENSAYO_TRABAJO (el ensayo E1) no escribe nada.
    /// </summary>
    [Fact]
    public void ShimSudo_ConDosHosts_DejaRastroOrdenadoPorHost()
    {
        var trabajo = DosHosts();
        string Ssh(string host, string comando) =>
            $"\"{Shim("ssh")}\" -p 22 -o BatchMode=yes ensayo@{host} '{comando}' || exit 11\n";

        var r = Bash($"export ENSAYO_TRABAJO=\"{trabajo}\"\n" +
            Ssh(HostNuevo, "sudo -n true") +
            Ssh(HostViejo, "sudo -n systemctl is-active stockapp-api") +
            Ssh(HostViejo, "sudo -n true"));

        Assert.True(r.ExitCode == 0, Diag(r));
        Assert.Equal(new[]
        {
            $"{HostNuevo} true",
            $"{HostViejo} systemctl is-active stockapp-api",
            $"{HostViejo} true",
        }, File.ReadAllLines(Path.Combine(trabajo, "sudo.rastro")));

        var (estado, raiz) = VpsViejoSimulado();
        var e1 = Bash(EnvShims(estado, raiz) + $"\"{Shim("sudo")}\" -n true");
        Assert.True(e1.ExitCode == 0, Diag(e1));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(estado)!, "*", SearchOption.AllDirectories),
            f => f.EndsWith("sudo.rastro"));
    }

    // ---------- Punta a punta de migrar-vps.sh sobre el arnés (spec §8.2) ----------
    //
    // Cada test arma su propio arnés (preparar-migracion: dos Postgres y dos APIs reales), corre
    // migrar-vps.sh REAL en sus modos y compara DATOS de las dos bases, no solo códigos de salida.
    // Al final: limpiar, docker ps igual al del principio, ninguna API del arnés viva y el HOME del
    // test vacío. El finally mata por PID exacto lo que quede y borra por nombre exacto.

    /// <summary>
    /// Un arnés de dos hosts. Bin va al frente del PATH con un docker del test que envuelve al real:
    /// corta todo comando que nombre stockapp-pg (el contenedor de desarrollo) y lo anota; y, si el
    /// test crea Bin/truncar-traida, entrega truncada UNA vez la traída del volcado del viejo (un
    /// fallo de transferencia real, después del stop: volcado-final-vps.sh sale con 4).
    /// </summary>
    private sealed record Arnes(string Trabajo, string Sufijo, string Home, string Bin, string[] PsAntes)
    {
        public string Viejo => $"stockapp-ensayo-origen-pg-{Sufijo}";
        public string Nuevo => $"stockapp-ensayo-pg-{Sufijo}";
        public string Corridas => Path.Combine(Trabajo, "operador", "stockapp-backups-vps", "corridas");
        public string BackupsOperador => Path.Combine(Trabajo, "operador", "stockapp-backups-vps");
        public string Raiz(string host) => Path.Combine(DirHost(Trabajo, host), "raiz");
        public string Estado(string host) => Path.Combine(DirHost(Trabajo, host), "estado");
        public string Migracion => Path.Combine(DirHost(Trabajo, HostNuevo), "home", "stockapp-deploy", "migracion");
    }

    private Arnes NuevoArnes()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var a = new Arnes(Path.Combine(DirectorioNuevo("ensayo-e2e-"), "trabajo"), sufijo,
            DirectorioNuevo("ensayo-e2e-home-"), DirectorioNuevo("ensayo-e2e-bin-"),
            ContenedoresCorriendo());
        var docker = Bash("command -v docker").Stdout.Trim();
        Assert.StartsWith("/", docker);
        BashLimpio.EscribirEjecutable(Path.Combine(a.Bin, "docker"),
            "#!/usr/bin/env bash\n" +
            "for a in \"$@\"; do\n" +
            "  if [[ \"$a\" == stockapp-pg ]]; then\n" +
            $"    printf '%s\\n' \"$*\" >> \"{a.Bin}/toco-stockapp-pg\"\n" +
            "    echo 'docker del test: stockapp-pg es el contenedor de desarrollo' >&2\n" +
            "    exit 97\n" +
            "  fi\n" +
            "done\n" +
            $"if [[ \"$1\" == exec && \"$2\" == {a.Viejo} && \"$3\" == cat ]] && mv \"{a.Bin}/truncar-traida\" \"{a.Bin}/traida-truncada\" 2>/dev/null; then\n" +
            $"  \"{docker}\" \"$@\" | head -c 2048\n" +
            "  exit 0\n" +
            "fi\n" +
            $"exec \"{docker}\" \"$@\"\n");
        return a;
    }

    private static void Preparar(Arnes a)
    {
        var r = BashLimpio.Ejecutar($"export NUGET_PACKAGES=\"{CacheNuget}\" PATH=\"{a.Bin}:$PATH\"\n" +
            $"\"{Script}\" preparar-migracion --trabajo \"{a.Trabajo}\" --sufijo {a.Sufijo} " +
            $"--puerto-viejo {PuertoLibre()} --puerto-nuevo {PuertoLibre()}", a.Home);
        Assert.True(r.ExitCode == 0, "preparar-migracion: " + Diag(r));
    }

    /// <summary>
    /// ensayo-local.sh migrar con la palabra (o las respuestas) por stdin y, si hace falta, una
    /// variable del arnés en el entorno de ese único comando.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) Migrar(Arnes a, string args, string stdin = "", string entorno = "") =>
        BashLimpio.Ejecutar($"export PATH=\"{a.Bin}:$PATH\"\n" +
            $"printf '%s' '{stdin}' | {entorno}\"{Script}\" migrar --trabajo \"{a.Trabajo}\" -- {args}", a.Home);

    private static string Psql(string contenedor, string sql)
    {
        Assert.StartsWith("stockapp-ensayo-", contenedor);
        var r = Bash($"docker exec -i {contenedor} psql -U stockapp -d stockapp -AtX -v ON_ERROR_STOP=1 <<'SQL'\n{sql}\nSQL");
        Assert.True(r.ExitCode == 0, $"psql en {contenedor}: " + Diag(r));
        return r.Stdout.Trim();
    }

    /// <summary>Filas de cada tabla de public, contadas de verdad (count(*), no estadísticas).</summary>
    private static Dictionary<string, long> Conteos(string contenedor) =>
        Psql(contenedor,
                "SELECT t || '=' || (xpath('/row/c/text()', query_to_xml(format('SELECT count(*) AS c FROM public.%I', t), false, true, '')))[1]::text " +
                "FROM (SELECT table_name::text AS t FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE') s ORDER BY t;")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('='))
            .ToDictionary(p => p[0], p => long.Parse(p[1]));

    /// <summary>
    /// Las tablas que la verificación (V8 hace un backup) puede hacer crecer en el nuevo: su huella se
    /// toma solo hasta el último Id del viejo, así las filas que ya existían se comparan por contenido
    /// y una pérdida no queda compensada por las filas nuevas.
    /// </summary>
    private static readonly string[] TablasQueCrecen = ["LogsAuditoria", "CorridasBackup"];

    /// <summary>El último Id del viejo en cada tabla de <see cref="TablasQueCrecen"/>.</summary>
    private static Dictionary<string, long> Topes(string contenedor) =>
        TablasQueCrecen.ToDictionary(t => t, t => long.Parse(Psql(contenedor, $"SELECT coalesce(max(\"Id\"), 0) FROM \"{t}\";")));

    /// <summary>
    /// Huella del CONTENIDO de cada tabla de public: md5 de todas sus filas (como texto, bytea
    /// incluido), en un orden estable. "Usuarios" sin UltimoAcceso (lo cambia un login); las tablas
    /// que crecen, hasta <paramref name="topes"/>.
    /// </summary>
    private static Dictionary<string, string> Huellas(string contenedor, IEnumerable<string> tablas, Dictionary<string, long> topes)
    {
        var sql = string.Join("\n", tablas.Select(t =>
        {
            var origen = t switch
            {
                "Usuarios" => "SELECT \"Id\", \"NombreUsuario\", \"NombreCompleto\", \"HashContrasena\", \"Rol\", \"Activo\", \"FechaAlta\" FROM \"Usuarios\"",
                _ when topes.TryGetValue(t, out var tope) => $"SELECT * FROM \"{t}\" WHERE \"Id\" <= {tope}",
                _ => $"SELECT * FROM \"{t}\"",
            };
            return $"SELECT '{t}=' || md5(coalesce(string_agg(x::text, E'\\n' ORDER BY x::text), '')) FROM ({origen}) x;";
        }));
        return Psql(contenedor, sql).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);
    }

    /// <summary>
    /// Datos de negocio reales en el viejo, con sus FK: un usuario con permisos, unidad, proveedor,
    /// rubro y fuente; un producto con stock y dos movimientos ligados a un gasto; el gasto con un
    /// adjunto de 1,6 MB de bytea aleatorio (va por TOAST); una tarea; y tres filas de auditoría.
    /// Devuelve el nombre del registro marcado en "Categorias".
    /// </summary>
    private static string SembrarViejo(Arnes a)
    {
        var s = a.Sufijo;
        Psql(a.Viejo, $$"""
            BEGIN;
            INSERT INTO "UnidadesMedida" ("Nombre", "Abreviatura") VALUES ('Unidad {{s}}', 'u');
            INSERT INTO "Proveedores" ("Nombre", "Telefono") VALUES ('Proveedor {{s}}', '099 123 456');
            INSERT INTO "RubrosGasto" ("Codigo", "Nombre") VALUES (101, 'Rubro {{s}}');
            INSERT INTO "FuentesFinanciamiento" ("Nombre", "Activo") VALUES ('Fuente {{s}}', true);
            INSERT INTO "Usuarios" ("NombreUsuario", "NombreCompleto", "HashContrasena", "Rol", "FechaAlta")
                VALUES ('operador{{s}}', 'Operador del ensayo', 'hash-de-ensayo', 1, '2026-10-01T12:00:00Z');
            INSERT INTO "PermisosUsuario" ("UsuarioId", "Permiso")
                SELECT "Id", p FROM "Usuarios", unnest(ARRAY['GestionarStock', 'GestionarGastos']) p WHERE "NombreUsuario" = 'operador{{s}}';
            INSERT INTO "Productos" ("Codigo", "Nombre", "UnidadMedidaId", "ProveedorId", "PrecioCosto", "StockActual", "StockMinimo", "FechaAlta")
                SELECT 'P-{{s}}', 'Producto {{s}}', u."Id", p."Id", 3.75, 87.5, 10, '2026-10-01T12:00:00Z'
                FROM "UnidadesMedida" u, "Proveedores" p WHERE u."Nombre" = 'Unidad {{s}}' AND p."Nombre" = 'Proveedor {{s}}';
            INSERT INTO "Gastos" ("ProveedorId", "NumeroFactura", "Detalle", "Fecha", "MontoTotal", "FuenteFinanciamientoId", "RubroGastoId", "CondicionPago")
                SELECT p."Id", 'F-{{s}}', 'Gasto {{s}}', '2026-10-02T12:00:00Z', 1234.56, f."Id", r."Id", 0
                FROM "Proveedores" p, "FuentesFinanciamiento" f, "RubrosGasto" r
                WHERE p."Nombre" = 'Proveedor {{s}}' AND f."Nombre" = 'Fuente {{s}}' AND r."Nombre" = 'Rubro {{s}}';
            INSERT INTO "MovimientosStock" ("ProductoId", "UsuarioId", "Tipo", "Cantidad", "PrecioUnitario", "Fecha", "Motivo", "Comentario", "GastoId")
                SELECT pr."Id", u."Id", v.t, v.c, 3.75, '2026-10-02T13:00:00Z', 0, 'movimiento {{s}}', g."Id"
                FROM "Productos" pr, "Usuarios" u, "Gastos" g, (VALUES (0, 100.0), (1, 12.5)) v(t, c)
                WHERE pr."Codigo" = 'P-{{s}}' AND u."NombreUsuario" = 'admin' AND g."NumeroFactura" = 'F-{{s}}';
            WITH contenido AS (
                SELECT decode(string_agg(md5(random()::text || i), '' ORDER BY i), 'hex') AS b FROM generate_series(1, 100000) i
            ), adjunto AS (
                INSERT INTO "Adjuntos" ("NombreArchivo", "ContentType", "TamanoBytes", "GastoId", "FechaAltaUtc")
                SELECT 'factura-{{s}}.pdf', 'application/pdf', length(c.b), g."Id", '2026-10-02T14:00:00Z'
                FROM contenido c, "Gastos" g WHERE g."NumeroFactura" = 'F-{{s}}'
                RETURNING "Id"
            )
            INSERT INTO "AdjuntosContenido" ("Id", "Contenido") SELECT a."Id", c.b FROM adjunto a, contenido c;
            INSERT INTO "Tareas" ("Titulo", "Descripcion", "Estado", "Prioridad", "CreadaPorUsuarioId", "FechaCreacion", "FechaLimite")
                SELECT 'Tarea {{s}}', 'Revisar el stock del ensayo', 0, 2, "Id", '2026-10-03T12:00:00Z', '2026-11-01T12:00:00Z'
                FROM "Usuarios" WHERE "NombreUsuario" = 'admin';
            INSERT INTO "LogsAuditoria" ("UsuarioId", "Fecha", "Accion", "Entidad", "EntidadId", "Detalle")
                SELECT u."Id", '2026-10-02T13:00:00Z', v.a, v.e, 1, 'auditoría {{s}} ' || v.e
                FROM "Usuarios" u, (VALUES (0, 'Producto'), (1, 'Gasto'), (0, 'Tarea')) v(a, e) WHERE u."NombreUsuario" = 'admin';
            COMMIT;
            """);
        var marca = "marca" + s;
        Marcar(a.Viejo, marca);
        return marca;
    }

    /// <summary>sha256 y largo del adjunto sembrado, leídos de la base (vacío si no está).</summary>
    private static string Adjunto(string contenedor, string sufijo) =>
        Psql(contenedor, "SELECT encode(sha256(c.\"Contenido\"), 'hex') || '|' || length(c.\"Contenido\") || '|' || a.\"TamanoBytes\" " +
            $"FROM \"Adjuntos\" a JOIN \"AdjuntosContenido\" c USING (\"Id\") WHERE a.\"NombreArchivo\" = 'factura-{sufijo}.pdf';");

    /// <summary>Un registro concreto que solo existe en la base donde el test lo escribió.</summary>
    private static string Marcar(string contenedor, string nombre) =>
        Psql(contenedor, $"INSERT INTO \"Categorias\" (\"Nombre\") VALUES ('{nombre}') RETURNING format('%s|%s|%s', \"Id\", \"Nombre\", \"Activo\");");

    private static string FilaDe(string contenedor, string nombre) =>
        Psql(contenedor, $"SELECT format('%s|%s|%s', \"Id\", \"Nombre\", \"Activo\") FROM \"Categorias\" WHERE \"Nombre\" = '{nombre}';");

    /// <summary>
    /// Las dos bases con los mismos datos (los sembrados por <see cref="SembrarViejo"/>):
    /// <list type="bullet">
    /// <item>el mismo conjunto de tablas y la misma cantidad de filas en cada una (las de
    /// <see cref="TablasQueCrecen"/> pueden tener más en el nuevo);</item>
    /// <item>el mismo CONTENIDO en cada tabla (<see cref="Huellas"/>): en las que crecen, todas las filas
    /// del viejo, por Id, iguales en el nuevo; en "Usuarios", todo salvo UltimoAcceso;</item>
    /// <item>el adjunto con el mismo sha256 y largo, y el registro marcado igual en las dos.</item>
    /// </list>
    /// </summary>
    private static void AfirmarMismosDatos(Arnes a, string marca, string contexto)
    {
        var viejo = Conteos(a.Viejo);
        var nuevo = Conteos(a.Nuevo);
        foreach (var (tabla, minimo) in new[] { ("Usuarios", 2), ("PermisosUsuario", 2), ("Productos", 1), ("MovimientosStock", 2),
                     ("Gastos", 1), ("Adjuntos", 1), ("AdjuntosContenido", 1), ("Tareas", 1), ("LogsAuditoria", 3), ("CorridasBackup", 1), ("Categorias", 1) })
            Assert.True(viejo[tabla] >= minimo, $"{contexto}: el viejo tiene {viejo[tabla]} filas en \"{tabla}\" y el test sembró al menos {minimo}");
        Assert.Equal(viejo.Keys.Order(), nuevo.Keys.Order());
        foreach (var (tabla, n) in viejo)
        {
            if (TablasQueCrecen.Contains(tabla))
                Assert.True(nuevo[tabla] >= n, $"{contexto}: \"{tabla}\" tiene {n} filas en el viejo y {nuevo[tabla]} en el nuevo");
            else
                Assert.True(nuevo[tabla] == n, $"{contexto}: \"{tabla}\" tiene {n} filas en el viejo y {nuevo[tabla]} en el nuevo");
        }
        var topes = Topes(a.Viejo);
        var huellasViejo = Huellas(a.Viejo, viejo.Keys, topes);
        var huellasNuevo = Huellas(a.Nuevo, viejo.Keys, topes);
        var distintas = viejo.Keys.Where(t => huellasViejo[t] != huellasNuevo[t]).ToArray();
        Assert.True(distintas.Length == 0, $"{contexto}: el contenido difiere entre el viejo y el nuevo en: {string.Join(", ", distintas)}");
        var adjunto = Adjunto(a.Viejo, a.Sufijo);
        Assert.Matches(@"^[0-9a-f]{64}\|1600000\|1600000$", adjunto);
        Assert.True(Adjunto(a.Nuevo, a.Sufijo) == adjunto, $"{contexto}: el adjunto del viejo ({adjunto}) no está igual en el nuevo");
        var fila = FilaDe(a.Viejo, marca);
        Assert.Matches($@"^\d+\|{marca}\|t$", fila);
        Assert.True(FilaDe(a.Nuevo, marca) == fila, $"{contexto}: el registro '{marca}' del viejo ({fila}) no está igual en el nuevo");
    }

    private static string Corrida(Arnes a, string modo)
    {
        var dirs = Directory.GetDirectories(a.Corridas, modo + "-*");
        Assert.True(dirs.Length == 1, $"se esperaba una corrida {modo}-* en {a.Corridas}: {string.Join(", ", dirs)}");
        return dirs[0];
    }

    private static string? Clave(string corrida, string clave) =>
        File.ReadAllLines(Path.Combine(corrida, "estado"))
            .Where(l => l.StartsWith(clave + "="))
            .Select(l => l[(clave.Length + 1)..])
            .LastOrDefault();

    private static string Leer(string archivo) => File.ReadAllText(archivo).Trim();

    private static int Puerto(Arnes a, string host) =>
        int.Parse(File.ReadAllLines(Path.Combine(a.Trabajo, "config"))
            .Single(l => l.StartsWith(host == HostViejo ? "PUERTO_VIEJO=" : "PUERTO_NUEVO="))
            .Split('=')[1]);

    private static string UnitDe(Arnes a, string host) =>
        Path.Combine(a.Raiz(host), "etc", "systemd", "system", "stockapp-api.service");

    /// <summary>El viejo congelado: inactive, enmascarado (symlink a /dev/null), la unit en .congelado y sin atender.</summary>
    private static void AfirmarViejoCongelado(Arnes a)
    {
        Assert.Equal("inactive", Leer(Path.Combine(a.Estado(HostViejo), "stockapp-api.activo")));
        Assert.Equal("masked", Leer(Path.Combine(a.Estado(HostViejo), "stockapp-api.habilitado")));
        Assert.Equal("/dev/null", new FileInfo(UnitDe(a, HostViejo)).LinkTarget);
        Assert.True(File.Exists(UnitDe(a, HostViejo) + ".congelado"), "la unit del viejo no está en .congelado");
        Assert.False(PuertoAtiende(Puerto(a, HostViejo)), "la API vieja sigue atendiendo");
    }

    /// <summary>El viejo sirviendo como antes: active, enabled, la unit como archivo regular y GET / OK.</summary>
    private static void AfirmarViejoSirviendo(Arnes a)
    {
        Assert.Equal("active", Leer(Path.Combine(a.Estado(HostViejo), "stockapp-api.activo")));
        Assert.Equal("enabled", Leer(Path.Combine(a.Estado(HostViejo), "stockapp-api.habilitado")));
        Assert.Null(new FileInfo(UnitDe(a, HostViejo)).LinkTarget);
        Assert.False(File.Exists(UnitDe(a, HostViejo) + ".congelado"), "quedó un .congelado en el viejo");
        Assert.True(PuertoAtiende(Puerto(a, HostViejo)), "la API vieja no atiende");
    }

    /// <summary>El rastro del sudo falso (ShimSudo_ConDosHosts_DejaRastroOrdenadoPorHost).</summary>
    private static string[] RastroSudo(Arnes a)
    {
        var archivo = Path.Combine(a.Trabajo, "sudo.rastro");
        return File.Exists(archivo) ? File.ReadAllLines(archivo) : [];
    }

    /// <summary>
    /// PIDs de toda API publicada de ESTE arnés que siga viva (también las de arranques anteriores), o
    /// null si ps falló. Sin asserts: lo usa también la limpieza de respaldo.
    /// </summary>
    private static int[]? PidsDeApis(Arnes a)
    {
        var dll = $"{a.Trabajo}/api/StockApp.Api.dll";
        var r = Bash("ps -eo pid=,args=");
        if (r.ExitCode != 0) return null;
        return r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Contains(dll))
            .Select(l => int.TryParse(l.Split(' ', 2)[0], out var p) ? p : 0)
            .Where(p => p > 0)
            .ToArray();
    }

    private static int[] ApisVivas(Arnes a) =>
        PidsDeApis(a) ?? throw new InvalidOperationException("ps -eo pid=,args= falló: no se puede saber si quedaron APIs vivas");

    /// <summary>limpiar, y la máquina como estaba: docker ps igual, ninguna API viva, nada del sufijo, HOME del test vacío.</summary>
    private static void AfirmarTerminaLimpio(Arnes a)
    {
        var limpiar = BashLimpio.Ejecutar($"export PATH=\"{a.Bin}:$PATH\"\n\"{Script}\" limpiar --trabajo \"{a.Trabajo}\"", a.Home);
        Assert.True(limpiar.ExitCode == 0, "limpiar: " + Diag(limpiar));
        Assert.Empty(ApisVivas(a));
        Assert.Equal(a.PsAntes, ContenedoresCorriendo());
        Assert.DoesNotContain(TodosLosContenedores(), n => n.EndsWith("-" + a.Sufijo));
        Assert.DoesNotContain(Lineas("docker volume ls --format '{{.Name}}'"), n => n.EndsWith("_" + a.Sufijo));
        Assert.False(File.Exists(Path.Combine(a.Bin, "toco-stockapp-pg")),
            "algo nombró stockapp-pg: " + (File.Exists(Path.Combine(a.Bin, "toco-stockapp-pg")) ? File.ReadAllText(Path.Combine(a.Bin, "toco-stockapp-pg")) : ""));
        Assert.Empty(Directory.EnumerateFileSystemEntries(a.Home));
    }

    /// <summary>
    /// Pase lo que pase: las APIs del arnés por PID exacto (su grupo: PID == PGID), limpiar (con el
    /// docker del test al frente, dentro del guardián de stockapp-pg) y los nombres exactos. Cada paso
    /// es best-effort y ninguno saltea a los siguientes ni tapa el error del test: acá no hay asserts.
    /// </summary>
    private static void LimpiarDeRespaldo(Arnes a)
    {
        static void Intentar(Action paso)
        {
            try { paso(); }
            catch (Exception) { /* limpieza de respaldo: el error que importa es el del test */ }
        }
        Intentar(() =>
        {
            foreach (var p in PidsDeApis(a) ?? [])
                Bash($"kill -KILL -- -{p} 2>/dev/null || kill -KILL {p}");
        });
        Intentar(() => MatarApisDeRespaldo(a.Trabajo));
        Intentar(() =>
        {
            if (File.Exists(Path.Combine(a.Trabajo, "config")))
                BashLimpio.Ejecutar($"export PATH=\"{a.Bin}:$PATH\"\n\"{Script}\" limpiar --trabajo \"{a.Trabajo}\"", a.Home);
        });
        Intentar(() => Bash($"docker rm -f -v {a.Nuevo} {a.Viejo} 2>/dev/null; " +
             $"docker volume rm stockapp_ensayo_origen_pgdata_{a.Sufijo} stockapp_ensayo_pgdata_{a.Sufijo} 2>/dev/null; true"));
    }

    /// <summary>--ensayo: GO, el nuevo con los datos del viejo y el viejo sirviendo exactamente como antes.</summary>
    [Fact]
    public void Migrar_Ensayo_Go_ViejoActive()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var marca = SembrarViejo(a);

            var r = Migrar(a, "--ensayo");

            Assert.True(r.ExitCode == 0, "--ensayo: " + Diag(r));
            Assert.Equal("GO", Clave(Corrida(a, "ensayo"), "resultado"));
            AfirmarMismosDatos(a, marca, "después del ensayo");
            AfirmarViejoSirviendo(a);
            Assert.True(PuertoAtiende(Puerto(a, HostNuevo)), "la API nueva no quedó arriba");
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// El corte con CONGELAR: GO, el viejo congelado (masked) y el nuevo con TODOS los datos del
    /// viejo, incluido un registro escrito justo antes. Los datos se comparan ANTES que el código de
    /// salida: un corte que no restaura tiene que fallar por los datos, no solo por una V.
    /// </summary>
    [Fact]
    public void Migrar_Corte_Go_ViejoMasked()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var marca = SembrarViejo(a);

            var r = Migrar(a, "", "CONGELAR\n");

            AfirmarMismosDatos(a, marca, $"después del corte (exit={r.ExitCode})");
            Assert.True(r.ExitCode == 0, "corte: " + Diag(r));
            var corte = Corrida(a, "corte");
            Assert.Equal("GO", Clave(corte, "resultado"));
            Assert.NotNull(Clave(corte, "congelado_utc"));
            AfirmarViejoCongelado(a);
            Assert.True(PuertoAtiende(Puerto(a, HostNuevo)), "la API nueva no quedó arriba");
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// Después de un corte, la contraseña del admin (la credencial real de V5/V8) y la de Postgres no
    /// aparecen en el rastro del sudo ni en NINGÚN archivo del trabajo del arnés, salvo en los que las
    /// guardan a propósito: admin-password y pg-password del arnés, el api.env de cada host (la cadena
    /// de conexión y, en el viejo, la contraseña de bootstrap) y el .env del servidor nuevo. Tampoco en
    /// la salida de migrar.
    /// </summary>
    [Fact]
    public void Migrar_Corte_SecretosNoQuedanEnElRastroNiEnElTrabajo()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            SembrarViejo(a);

            var r = Migrar(a, "", "CONGELAR\n");

            Assert.True(r.ExitCode == 0, "corte: " + Diag(r));
            var secretos = new[] { "admin-password", "pg-password" }
                .Select(n => (Nombre: n, Valor: File.ReadAllText(Path.Combine(a.Trabajo, n)).Trim()))
                .ToArray();
            Assert.All(secretos, x => Assert.True(x.Valor.Length >= 20, $"{x.Nombre} del arnés es demasiado corto para buscarlo"));
            var guardados = new[]
            {
                Path.Combine(a.Trabajo, "admin-password"),
                Path.Combine(a.Trabajo, "pg-password"),
                Path.Combine(a.Raiz(HostViejo), "etc", "stockapp-api", "api.env"),
                Path.Combine(a.Raiz(HostNuevo), "etc", "stockapp-api", "api.env"),
                Path.Combine(DirHost(a.Trabajo, HostNuevo), "home", "stockapp-deploy", ".env"),
            };
            var rastro = Path.Combine(a.Trabajo, "sudo.rastro");
            Assert.True(File.Exists(rastro) && RastroSudo(a).Length > 0, "el corte no dejó rastro del sudo");
            // Solo archivos regulares: el trabajo tiene también el symlink del mask y los sockets de
            // diagnóstico de dotnet (en el /tmp de cada host simulado).
            var listado = Bash($"find \"{a.Trabajo}\" -type f -print");
            Assert.True(listado.ExitCode == 0, Diag(listado));
            var archivos = listado.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(f => !guardados.Contains(f))
                .ToArray();
            Assert.Contains(rastro, archivos);
            Assert.Contains(archivos, f => f.EndsWith("registro.log"));
            foreach (var f in archivos)
            {
                var bytes = File.ReadAllBytes(f);
                foreach (var (nombre, valor) in secretos)
                    Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(valor)) < 0,
                        $"el valor de {nombre} aparece en {f}");
            }
            foreach (var (nombre, valor) in secretos)
                Assert.DoesNotContain(valor, r.Stdout + r.Stderr);
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// --revertir después de un corte GO: el viejo vuelve a servir con sus datos intactos, el nuevo
    /// queda detenido y deshabilitado, y la corrida del corte cierra REVERTIDO. Con el shim fiel, un
    /// --revertir que no devuelve la unit a su lugar falla el start con 5.
    /// </summary>
    [Fact]
    public void Migrar_RevertirTrasCorte_ViejoActiveNuevoInactive()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var marca = SembrarViejo(a);
            var conteosViejo = Conteos(a.Viejo);
            var huellasViejo = Huellas(a.Viejo, conteosViejo.Keys, []);
            var corte = Migrar(a, "", "CONGELAR\n");
            Assert.True(corte.ExitCode == 0, "corte: " + Diag(corte));
            AfirmarViejoCongelado(a);

            var r = Migrar(a, "--revertir", "REVERTIR\n");

            Assert.True(r.ExitCode == 0, "--revertir: " + Diag(r));
            AfirmarViejoSirviendo(a);
            Assert.Equal("inactive", Leer(Path.Combine(a.Estado(HostNuevo), "stockapp-api.activo")));
            Assert.Equal("disabled", Leer(Path.Combine(a.Estado(HostNuevo), "stockapp-api.habilitado")));
            Assert.False(PuertoAtiende(Puerto(a, HostNuevo)), "la API nueva sigue atendiendo");
            Assert.Equal("REVERTIDO", Clave(Corrida(a, "corte"), "resultado"));
            Assert.Equal("GO", Clave(Corrida(a, "corte"), "resultado_previo"));
            Assert.Equal("REVERTIDO", Clave(Corrida(a, "revertir"), "resultado"));
            // El viejo vuelve con lo que tenía: nada se perdió ni se agregó en la ida y vuelta.
            Assert.Equal(conteosViejo, Conteos(a.Viejo));
            Assert.Equal(huellasViejo, Huellas(a.Viejo, conteosViejo.Keys, []));
            Assert.Matches($@"^\d+\|{marca}\|t$", FilaDe(a.Viejo, marca));
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// El --revertir REAL tras un corte, mirado en el rastro del sudo falso: la API nueva se detiene
    /// antes de tocar el viejo, y en el viejo el orden es unmask &lt; mv del .congelado a su lugar &lt;
    /// daemon-reload &lt; start, una vez cada uno. Al final el viejo queda active.
    /// </summary>
    [Fact]
    public void Revertir_OrdenEnElViejo_UnmaskMvReloadStart()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var corte = Migrar(a, "", "CONGELAR\n");
            Assert.True(corte.ExitCode == 0, "corte: " + Diag(corte));
            var desde = RastroSudo(a).Length;

            var r = Migrar(a, "--revertir", "REVERTIR\n");

            Assert.True(r.ExitCode == 0, "--revertir: " + Diag(r));
            var rastro = RastroSudo(a).Skip(desde).ToList();
            var unit = UnitDe(a, HostViejo);
            int Indice(string linea)
            {
                var i = rastro.FindIndex(l => l == linea);
                Assert.True(i >= 0, $"falta '{linea}' en el rastro del --revertir:\n{string.Join('\n', rastro)}");
                Assert.True(rastro.Count(l => l == linea) == 1, $"'{linea}' aparece más de una vez:\n{string.Join('\n', rastro)}");
                return i;
            }
            var stopNuevo = Indice($"{HostNuevo} systemctl stop stockapp-api");
            var unmask = Indice($"{HostViejo} systemctl unmask stockapp-api");
            var mv = Indice($"{HostViejo} mv {unit}.congelado {unit}");
            var reload = Indice($"{HostViejo} systemctl daemon-reload");
            var start = Indice($"{HostViejo} systemctl start stockapp-api");
            Assert.True(stopNuevo < unmask, $"el viejo se modificó antes de detener el nuevo:\n{string.Join('\n', rastro)}");
            Assert.True(unmask < mv && mv < reload && reload < start, $"orden equivocado en el viejo:\n{string.Join('\n', rastro)}");
            AfirmarViejoSirviendo(a);
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// Corte con un fallo inyectado (solo arnés) después de congelar: en el 7 (identificar el
    /// volcado) y el 9 (restaurar, como un 5) sale 6, en el 11 (arrancar) sale 7; la corrida queda
    /// abierta y el viejo congelado. --continuar retoma y da GO con los datos del viejo en el nuevo.
    /// </summary>
    [Theory]
    [InlineData(7, 6)]
    [InlineData(9, 6)]
    [InlineData(11, 7)]
    public void Migrar_CorteConFalloInyectado_ContinuarDaGo(int paso, int exitDelCorte)
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var marca = SembrarViejo(a);

            var corte = Migrar(a, "", "CONGELAR\n", $"STOCKAPP_ENSAYO_FALLAR_EN={paso} ");

            Assert.True(corte.ExitCode == exitDelCorte, $"corte con fallo en el {paso}: " + Diag(corte));
            var corrida = Corrida(a, "corte");
            Assert.Null(Clave(corrida, "resultado"));
            Assert.Equal("1", Clave(corrida, $"fallo_inyectado_{paso}"));
            Assert.Null(Clave(corrida, $"paso_{paso}"));
            Assert.Contains($"--continuar {corrida}", corte.Stdout + corte.Stderr);
            AfirmarViejoCongelado(a);

            var r = Migrar(a, $"--continuar \"{corrida}\"");

            AfirmarMismosDatos(a, marca, $"después de --continuar (exit={r.ExitCode})");
            Assert.True(r.ExitCode == 0, "--continuar: " + Diag(r));
            Assert.Equal("GO", Clave(corrida, "resultado"));
            Assert.StartsWith("ok ", Clave(corrida, $"paso_{paso}"));
            AfirmarViejoCongelado(a);
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// Un fallo REAL del volcado después del stop (la traída llega truncada: volcado-final-vps.sh sale
    /// con 4) deja el viejo congelado y la corrida abierta. El primer --continuar vuelve a volcar en
    /// una carpeta NUEVA (volcado-2/) y cae en el 7 (inyectado); el segundo retoma en el 7 con la
    /// carpeta que dice el estado (volcado_dir), nunca con la vacía del intento fallido, y da GO.
    /// </summary>
    [Fact]
    public void Migrar_VolcadoFallaTrasElStop_ContinuarVuelcaEnOtraCarpetaYDaGo()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var marca = SembrarViejo(a);
            File.WriteAllText(Path.Combine(a.Bin, "truncar-traida"), "");

            var corte = Migrar(a, "", "CONGELAR\n");

            Assert.True(corte.ExitCode == 6, "corte con la traída truncada: " + Diag(corte));
            Assert.True(File.Exists(Path.Combine(a.Bin, "traida-truncada")), "la traída no pasó por el docker del test");
            Assert.Contains("volcado-final-vps.sh salió con 4", corte.Stdout + corte.Stderr);
            var corrida = Corrida(a, "corte");
            Assert.Null(Clave(corrida, "resultado"));
            Assert.Empty(Directory.GetFiles(Path.Combine(corrida, "volcado"), "volcado-*.dump"));
            AfirmarViejoCongelado(a);

            var segundo = Migrar(a, $"--continuar \"{corrida}\"", entorno: "STOCKAPP_ENSAYO_FALLAR_EN=7 ");
            Assert.True(segundo.ExitCode == 6, "primer --continuar (fallo en el 7): " + Diag(segundo));
            Assert.Equal("volcado-2", Clave(corrida, "volcado_dir"));
            Assert.Single(Directory.GetFiles(Path.Combine(corrida, "volcado-2"), "volcado-*.dump"));

            var r = Migrar(a, $"--continuar \"{corrida}\"");

            AfirmarMismosDatos(a, marca, $"después del segundo --continuar (exit={r.ExitCode})");
            Assert.True(r.ExitCode == 0, "segundo --continuar: " + Diag(r));
            Assert.Equal("GO", Clave(corrida, "resultado"));
            var volcado = Clave(corrida, "volcado");
            Assert.Equal(Path.GetFileName(Directory.GetFiles(Path.Combine(corrida, "volcado-2"), "volcado-*.dump").Single()), volcado);
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(corrida, "volcado-2", volcado!)))),
                Clave(corrida, "sha256"));
            AfirmarViejoCongelado(a);
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// --desde-backup con el volcado de una corrida anterior (el de un --ensayo): GO reducido (sin
    /// viejo: los pasos 5, 6, 7 y 10 y V10 OMITIDO), sin un solo comando como root en el viejo, y el
    /// nuevo con EXACTAMENTE ese dump: tiene lo escrito antes del ensayo, no lo escrito en el viejo
    /// después, y perdió lo que tenía el nuevo.
    /// </summary>
    [Fact]
    public void Migrar_DesdeBackup_DumpDeOtraCorrida_GoReducido()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var antes = SembrarViejo(a);
            var ensayo = Migrar(a, "--ensayo");
            Assert.True(ensayo.ExitCode == 0, "--ensayo: " + Diag(ensayo));
            // El viejo tal como quedó en el volcado del ensayo (nadie escribe en él entre el volcado y acá).
            var conteosDelDump = Conteos(a.Viejo);
            var topesDelDump = Topes(a.Viejo);
            var huellasDelDump = Huellas(a.Viejo, conteosDelDump.Keys, topesDelDump);
            var adjuntoDelDump = Adjunto(a.Viejo, a.Sufijo);
            var volcado = Path.Combine(Corrida(a, "ensayo"), "volcado");
            var dump = Directory.GetFiles(volcado, "volcado-*.dump").Single();
            var manifiesto = Directory.GetFiles(volcado, "volcado-*.manifiesto").Single();
            var despues = "despues" + a.Sufijo;
            var enElNuevo = "nuevo" + a.Sufijo;
            Marcar(a.Viejo, despues);
            Marcar(a.Nuevo, enElNuevo);
            var conteosViejo = Conteos(a.Viejo);
            var pidViejo = PidDe(a.Trabajo, HostViejo);
            var desde = RastroSudo(a).Length;

            var r = Migrar(a, $"--desde-backup \"{dump}\" --manifiesto \"{manifiesto}\"", "RESTAURAR\n");

            Assert.True(r.ExitCode == 0, "--desde-backup: " + Diag(r));
            var corrida = Corrida(a, "desde-backup");
            Assert.Equal("GO", Clave(corrida, "resultado"));
            foreach (var p in new[] { 5, 6, 7, 10 })
                Assert.Matches($@"\(\s*{p}/13\) .*OMITIDO", r.Stdout);
            Assert.Matches(@"V10 .*OMITIDO", r.Stdout);
            Assert.Matches(@"V6 .*OK", r.Stdout);
            Assert.DoesNotContain(RastroSudo(a).Skip(desde), l => l.StartsWith(HostViejo + " "));
            Assert.Equal(pidViejo, PidDe(a.Trabajo, HostViejo));
            AfirmarViejoSirviendo(a);

            Assert.Equal(FilaDe(a.Viejo, antes), FilaDe(a.Nuevo, antes));
            Assert.Equal("", FilaDe(a.Nuevo, despues));
            Assert.Equal("", FilaDe(a.Nuevo, enElNuevo));
            var conteosNuevo = Conteos(a.Nuevo);
            Assert.Equal(conteosViejo["Categorias"] - 1, conteosNuevo["Categorias"]);
            // El nuevo tiene EXACTAMENTE el contenido del dump del ensayo, tabla por tabla.
            var huellasNuevo = Huellas(a.Nuevo, conteosDelDump.Keys, topesDelDump);
            var distintas = conteosDelDump.Keys.Where(t => huellasDelDump[t] != huellasNuevo[t]).ToArray();
            Assert.True(distintas.Length == 0, $"el contenido del nuevo difiere del dump del ensayo en: {string.Join(", ", distintas)}");
            foreach (var (tabla, n) in conteosDelDump.Where(kv => !TablasQueCrecen.Contains(kv.Key)))
                Assert.True(conteosNuevo[tabla] == n, $"\"{tabla}\": {n} filas en el dump y {conteosNuevo[tabla]} en el nuevo");
            Assert.Matches(@"^[0-9a-f]{64}\|1600000\|1600000$", adjuntoDelDump);
            Assert.Equal(adjuntoDelDump, Adjunto(a.Nuevo, a.Sufijo));
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
        }
    }

    /// <summary>
    /// --post-corte --antes-de-plazo tras un ensayo y un corte GO, con las palabras por stdin: BORRAR
    /// para el plazo, y después se conserva el directorio del corte en el nuevo, se borra el del
    /// ensayo y se conserva la corrida local del ensayo. El dump y el manifiesto del corte quedan
    /// archivados en corte-&lt;ts&gt; (700) con el sha256 del estado, y solo se borró lo confirmado.
    /// </summary>
    [Fact]
    public void Migrar_PostCorteAntesDePlazo_ArchivaYBorraSoloLoConfirmado()
    {
        var a = NuevoArnes();
        try
        {
            Preparar(a);
            var marca = SembrarViejo(a);
            var ensayo = Migrar(a, "--ensayo");
            Assert.True(ensayo.ExitCode == 0, "--ensayo: " + Diag(ensayo));
            var corte = Migrar(a, "", "CONGELAR\n");
            Assert.True(corte.ExitCode == 0, "corte: " + Diag(corte));
            var corrida = Corrida(a, "corte");
            var corridaEnsayo = Corrida(a, "ensayo");
            var nombreCorte = Path.GetFileName(corrida);
            var nombreEnsayo = Path.GetFileName(corridaEnsayo);
            var volcado = Clave(corrida, "volcado")!;
            var manifiesto = volcado.Replace(".dump", ".manifiesto");
            Assert.True(Directory.Exists(Path.Combine(a.Migracion, nombreCorte)));
            Assert.True(Directory.Exists(Path.Combine(a.Migracion, nombreEnsayo)));

            // Los directorios del nuevo se ofrecen en orden (corte-* < ensayo-*), después las corridas locales.
            var r = Migrar(a, $"--post-corte \"{corrida}\" --antes-de-plazo", "BORRAR\nno\nBORRAR\nno\n");

            Assert.True(r.ExitCode == 0, "--post-corte: " + Diag(r));
            var archivo = Path.Combine(a.BackupsOperador, nombreCorte);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(archivo));
            var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(archivo, volcado))));
            Assert.Equal(Clave(corrida, "sha256"), sha);
            Assert.Equal($"{sha}  {volcado}", Leer(Path.Combine(archivo, volcado + ".sha256")));
            Assert.True(File.Exists(Path.Combine(archivo, manifiesto)), "el manifiesto no quedó en el archivo");
            Assert.True(File.Exists(Path.Combine(archivo, "estado")) && File.Exists(Path.Combine(archivo, "registro.log")));
            Assert.False(File.Exists(Path.Combine(corrida, "volcado", volcado)), "el dump quedó también en la corrida");
            Assert.True(File.Exists(Path.Combine(corrida, "estado")), "la corrida del corte se borró");

            Assert.True(Directory.Exists(Path.Combine(a.Migracion, nombreCorte)), "se borró el directorio del corte en el nuevo (no se confirmó)");
            Assert.False(Directory.Exists(Path.Combine(a.Migracion, nombreEnsayo)), "no se borró el directorio del ensayo en el nuevo (se confirmó)");
            Assert.True(Directory.Exists(corridaEnsayo), "se borró la corrida local del ensayo (no se confirmó)");
            Assert.Contains("1 borrados, 2 conservados", r.Stdout);
            // Archivar y borrar copias no tocó los datos: el nuevo sigue con los del viejo.
            AfirmarMismosDatos(a, marca, "después del --post-corte");
            AfirmarViejoCongelado(a);
            AfirmarTerminaLimpio(a);
        }
        finally
        {
            LimpiarDeRespaldo(a);
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
