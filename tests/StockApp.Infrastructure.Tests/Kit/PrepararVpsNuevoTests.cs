using System.Text.RegularExpressions;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/preparar-vps-nuevo.sh (spec 6.4, pasos 0 a 2): el pre-vuelo, el motor de
/// pasos idempotente por estado observado, Docker y el usuario del servicio. Cada test corre el
/// script REAL contra un <see cref="HostSimulado"/> (contenedor ubuntu:24.04 con shims con estado)
/// y un árbol temporal con la forma del repo; el ssh, scp, curl y getent del operador son los
/// falsos del host. Ningún test toca un servidor real ni el stockapp-pg de desarrollo.
///
/// Los bloques que preparar llama más adelante (deploy-vps.sh y publicar-release.sh) son
/// ejecutables falsos del árbol: registran sus argumentos y el STOCKAPP_ENV_LOCAL con el que los
/// invocaron en el rastro del operador y salen con STUB_DEPLOY_EXIT / STUB_PUBLICAR_EXIT.
/// </summary>
public class PrepararVpsNuevoTests
{
    private const string Dominio = "api.test.invalid";
    private const string Version = "1.2.3";
    private const string ClaveValida = "MCowBQYDK2VwAyEAabcdefghijklmnopqrstuvwxyz012345678=";

    // Marcador: en el diccionario de entorno de Correr, este valor quita la variable.
    private const string Quitar = "<quitar>";

    private static readonly string[] ArgsBase = ["--version", Version, "--env-viejo", "{ENV_VIEJO}"];

    private static string Sq(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Host simulado con el dominio de la API resolviendo a su IP (lo normal).</summary>
    private static HostSimulado CrearHost()
    {
        var h = HostSimulado.Crear();
        h.ResolverDominio(Dominio);
        return h;
    }

    internal sealed record Resultado(int Exit, string Stdout, string Stderr)
    {
        public string Todo => $"--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}";
    }

    /// <summary>Árbol temporal con la forma del repo, con el script real y bloques falsos.</summary>
    internal sealed class Arbol : IDisposable
    {
        private readonly HostSimulado _host;
        private readonly Dictionary<string, string?> _envNuevo;

        public Arbol(HostSimulado host)
        {
            _host = host;
            var raizRepo = BashLimpio.RaizRepo();
            var id = Guid.NewGuid().ToString("N")[..12];
            Raiz = Path.Combine(Path.GetTempPath(), "stockapp-prep-" + id);
            Home = Path.Combine(Path.GetTempPath(), "stockapp-prep-home-" + id);
            Directory.CreateDirectory(Home);

            Copiar(raizRepo, "deploy/preparar-vps-nuevo.sh", ejecutable: true);
            Copiar(raizRepo, "deploy/lib/orquestacion.sh");
            Copiar(raizRepo, "deploy/tls/stockapp-tls-deploy-hook.sh", ejecutable: true);
            Copiar(raizRepo, "deploy/docker-compose.postgres.yml");

            Directory.CreateDirectory(Path.Combine(Raiz, "releases", "win"));
            File.WriteAllText(Path.Combine(Raiz, "releases", "win", "releases.win.json"),
                $"{{\"Assets\":[{{\"Type\":\"Full\",\"Version\":\"{Version}\",\"FileName\":\"StockApp-{Version}-full.nupkg\"}}]}}\n");
            File.WriteAllText(Path.Combine(Raiz, "releases", "win", "StockAppSetup.exe"), "");

            EscribirBloqueFalso("deploy/deploy-vps.sh", "deploy-vps.sh", "STUB_DEPLOY_EXIT", creaLaUnit: true);
            EscribirBloqueFalso("build/publicar-release.sh", "publicar-release.sh", "STUB_PUBLICAR_EXIT", creaLaUnit: false);

            EnvViejo = Path.Combine(Raiz, "env-viejo.txt");
            File.WriteAllText(EnvViejo, $"LICENCIA_CLAVE_PUBLICA_BASE64={ClaveValida}\n");

            _envNuevo = new Dictionary<string, string?>
            {
                ["VPS_HOST"] = host.Host,
                ["VPS_USER"] = HostSimulado.Usuario,
                ["VPS_SSH_PORT"] = host.PuertoSsh.ToString(),
                ["API_SCHEME"] = "https",
                ["API_HOSTNAME"] = Dominio,
                ["API_PORT"] = "8443",
            };
            EscribirEnvNuevo();
        }

        public string Raiz { get; }
        public string Home { get; }
        public string EnvViejo { get; }
        public string EnvNuevo => Path.Combine(Raiz, "deploy", ".env.vps-nuevo");
        public string Script => Path.Combine(Raiz, "deploy", "preparar-vps-nuevo.sh");

        private void Copiar(string raizRepo, string relativo, bool ejecutable = false)
        {
            var destino = Path.Combine(Raiz, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(Path.Combine(raizRepo, relativo), destino, overwrite: true);
            if (ejecutable) File.SetUnixFileMode(destino, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        private void EscribirBloqueFalso(string relativo, string nombre, string variableDeSalida, bool creaLaUnit)
        {
            var crear = creaLaUnit
                ? "if [[ \"$rc\" == 0 ]]; then\n" +
                  "    source \"$HS_DIR/hostsim.env\"\n" +
                  "    docker exec -u 0 \"$HS_CONTENEDOR\" install -D -m 0644 /dev/null /etc/systemd/system/stockapp-api.service\n" +
                  "fi\n"
                : "";
            BashLimpio.EscribirEjecutable(Path.Combine(Raiz, relativo),
                "#!/usr/bin/env bash\n" +
                $"# Bloque falso de los tests: {nombre}.\n" +
                "set -euo pipefail\n" +
                $"{{ printf 'BLOQUE {nombre}'; printf ' %q' \"$@\"; printf ' ENV=%s\\n' \"${{STOCKAPP_ENV_LOCAL:-}}\"; }} >> \"$HS_DIR/rastro.txt\"\n" +
                $"rc=\"${{{variableDeSalida}:-0}}\"\n" +
                crear +
                "exit \"$rc\"\n");
        }

        /// <summary>Cambia claves del .env del VPS nuevo (valor null: la quita) y lo reescribe.</summary>
        public void ConfigurarEnvNuevo(Action<Dictionary<string, string?>> ajuste)
        {
            ajuste(_envNuevo);
            EscribirEnvNuevo();
        }

        private void EscribirEnvNuevo()
        {
            Directory.CreateDirectory(Path.Combine(Raiz, "deploy"));
            var lineas = _envNuevo.Where(kv => kv.Value is not null).Select(kv => $"{kv.Key}={kv.Value}");
            File.WriteAllText(EnvNuevo, string.Join('\n', lineas) + "\n");
        }

        public Resultado Correr(string[] args, IDictionary<string, string>? env = null, bool conLockTomado = false)
        {
            var exportes = new List<string> { $"export HOME={Sq(Home)}", $"export HS_DIR={Sq(_host.DirOperador)}" };
            var entorno = new Dictionary<string, string>(env ?? new Dictionary<string, string>());
            if (!entorno.ContainsKey("STOCKAPP_ENV_LOCAL")) entorno["STOCKAPP_ENV_LOCAL"] = EnvNuevo;
            foreach (var (clave, valor) in entorno)
            {
                exportes.Add(valor == Quitar
                    ? $"unset {clave}"
                    : $"export {clave}={Sq(valor.Replace("{ENV_NUEVO}", EnvNuevo))}");
            }

            var argumentos = string.Join(' ', args.Select(a => Sq(a.Replace("{ENV_VIEJO}", EnvViejo))));
            // Lock tomado por "otro proceso" (el padre, como migrar): el hijo corre con el fd 9
            // cerrado, igual que orq_bloque, así que su lock es otra descripción y choca.
            var lock_ = conLockTomado
                ? "mkdir -p \"$HOME/stockapp-backups-vps\"\nexec 9>>\"$HOME/stockapp-backups-vps/.migracion.lock\"\nflock -n 9\n"
                : "";
            var (exit, stdout, stderr) = _host.Operador(
                string.Join('\n', exportes) + $"\n{lock_}cd {Sq(Raiz)}\n./deploy/preparar-vps-nuevo.sh {argumentos}" +
                (conLockTomado ? " 9>&-" : ""));
            return new Resultado(exit, stdout, stderr);
        }

        public void Dispose()
        {
            if (Directory.Exists(Raiz)) Directory.Delete(Raiz, recursive: true);
            if (Directory.Exists(Home)) Directory.Delete(Home, recursive: true);
        }
    }

    /// <summary>Árbol nuevo, una corrida y listo (los tests que corren dos veces usan <see cref="Arbol"/>).</summary>
    internal static Resultado Correr(HostSimulado h, string[] args, IDictionary<string, string>? env = null)
    {
        using var arbol = new Arbol(h);
        return arbol.Correr(args, env);
    }

    private static string[] ConBase(params string[] extra) => [.. ArgsBase, .. extra];

    private static string[] LineasAdicionales(string antes, string despues) =>
        despues.Length >= antes.Length && despues.StartsWith(antes, StringComparison.Ordinal)
            ? despues[antes.Length..].Split('\n', StringSplitOptions.RemoveEmptyEntries)
            : despues.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Los ssh del rastro, sin el escapado de `printf %q` (los espacios del comando vienen como
    /// `\ `): lo que se lee es el comando tal como llegó al host.
    /// </summary>
    private static string[] LineasSsh(HostSimulado h) =>
        h.Rastro().Split('\n')
            .Where(l => l.StartsWith("ARGS:", StringComparison.Ordinal))
            .Select(l => l.Replace("\\", ""))
            .ToArray();

    private static void SinSsh(HostSimulado h) =>
        Assert.DoesNotContain("ARGS:", h.Rastro());

    // ---------- Uso ----------

    [Theory]
    [InlineData("sinversion")]
    [InlineData("versionvacia")]
    [InlineData("versionrara")]
    [InlineData("opciondesconocida")]
    [InlineData("envviejosinvalor")]
    public void Uso_ArgumentosInvalidos_Aborta2SinSsh(string caso)
    {
        using var h = CrearHost();
        string[] args = caso switch
        {
            "sinversion" => ["--env-viejo", "{ENV_VIEJO}"],
            "versionvacia" => ["--version", "", "--env-viejo", "{ENV_VIEJO}"],
            "versionrara" => ["--version", "1.2.3; rm -rf x", "--env-viejo", "{ENV_VIEJO}"],
            "opciondesconocida" => [.. ArgsBase, "--hacer-magia"],
            _ => ["--version", Version, "--env-viejo"],
        };

        var r = Correr(h, args);

        Assert.True(r.Exit == 2, r.Todo);
        SinSsh(h);
    }

    [Fact]
    public void Encabezado_LlevaLaTablaDeCodigosDePreparar()
    {
        var texto = File.ReadAllText(Path.Combine(BashLimpio.RaizRepo(), "deploy", "preparar-vps-nuevo.sh"));

        Assert.Contains("# Códigos de salida (estables):", texto);
        Assert.Matches(@"(?m)^#\s+0\s+el VPS quedó listo \(o ya lo estaba\)$", texto);
        Assert.Matches(@"(?m)^#\s+2\s+uso / argumentos$", texto);
        Assert.Matches(@"(?m)^#\s+3\s+precondición \(config, `sudo -n`, versión del SO, DNS, falta `releases/win`, lock\): no se tocó nada$", texto);
        Assert.Matches(@"(?m)^#\s+5\s+falló un paso: lo hecho queda hecho y re-correr continúa$", texto);
        Assert.Matches(@"(?m)^#\s+6\s+el re-chequeo de acceso después de `ufw enable` falló: la reversa automática deshace el firewall en N segundos; esperar y re-correr$", texto);
        Assert.Matches(@"(?m)^#\s+8\s+no se pudo consultar \(ssh 255, salida ilegible\)$", texto);
        Assert.Matches(@"(?m)^#\s+9\s+alcance: el destino no parece un VPS dedicado \(contenedores ajenos, puerto 80 ocupado\) o es el VPS viejo$", texto);
    }

    // ---------- Pre-vuelo: identidad (A9) ----------

    [Theory]
    [InlineData("sin")]
    [InlineData("absoluta")]
    [InlineData("relativa")]
    public void SinStockappEnvLocal_O_IgualADeployEnv_Aborta3SinSsh(string caso)
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        File.WriteAllText(Path.Combine(arbol.Raiz, "deploy", ".env"), "API_PORT=8080\n");
        var env = caso switch
        {
            "sin" => new Dictionary<string, string> { ["STOCKAPP_ENV_LOCAL"] = Quitar },
            "absoluta" => new Dictionary<string, string> { ["STOCKAPP_ENV_LOCAL"] = Path.Combine(arbol.Raiz, "deploy", ".env") },
            _ => new Dictionary<string, string> { ["STOCKAPP_ENV_LOCAL"] = "deploy/.env" },
        };

        var r = arbol.Correr(ConBase(), env);

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("STOCKAPP_ENV_LOCAL", r.Stderr);
        SinSsh(h);
    }

    [Fact]
    public void VpsHostIgualAlDeDeployEnv_Aborta9SinSsh()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        File.WriteAllText(Path.Combine(arbol.Raiz, "deploy", ".env"), $"VPS_HOST={h.Host}\nVPS_USER=otro\nVPS_SSH_PORT=22\n");

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Contains("VPS viejo", r.Stderr);
        SinSsh(h);
    }

    [Fact]
    public void VpsHostDistintoAlDeDeployEnv_Sigue()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        File.WriteAllText(Path.Combine(arbol.Raiz, "deploy", ".env"), "VPS_HOST=viejo.test.invalid\nVPS_USER=otro\nVPS_SSH_PORT=22\n");

        var r = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
    }

    // ---------- Pre-vuelo: configuración, DNS, releases y licencia ----------

    [Theory]
    [InlineData("API_HOSTNAME", null)]
    [InlineData("API_SCHEME", "http")]
    [InlineData("API_SCHEME", null)]
    [InlineData("API_PORT", null)]
    [InlineData("API_PORT", "ochenta")]
    [InlineData("VPS_USER", null)]
    [InlineData("VPS_SSH_PORT", null)]
    [InlineData("VPS_HOST", null)]
    public void Prevuelo_FaltaApiHostnameOSchemeNoHttps_Aborta3(string clave, string? valor)
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        arbol.ConfigurarEnvNuevo(e => e[clave] = valor);

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains(clave, r.Stderr);
        SinSsh(h);
    }

    [Fact]
    public void Prevuelo_SinSudoN_Aborta3()
    {
        using var h = CrearHost();
        h.Escribir("/var/lib/hostsim/sudo-pide-contrasena", "");

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("sudo", r.Stderr);
        Assert.Equal(0, Regex.Matches(h.RastroHost(), "apt-get").Count);
    }

    [Fact]
    public void Prevuelo_NoEsUbuntu2404_Aborta3()
    {
        using var h = CrearHost();
        h.Escribir("/etc/os-release", "NAME=\"Ubuntu\"\nVERSION_ID=\"22.04\"\n");

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("24.04", r.Stderr);
        Assert.Equal(0, Regex.Matches(h.RastroHost(), "apt-get").Count);
    }

    [Fact]
    public void Prevuelo_PuertoSshNoEscucha_Aborta3()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        // El ssh llega por una conexión ya establecida (ControlMaster) al puerto 2200, pero el
        // host no tiene a nadie escuchando ahí: el `ss -ltn` del host es lo que lo delata.
        File.WriteAllText(Path.Combine(h.DirOperador, $"maestro-{HostSimulado.Usuario}-2200"), "");
        arbol.ConfigurarEnvNuevo(e => e["VPS_SSH_PORT"] = "2200");

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("2200", r.Stderr);
        Assert.Equal(0, Regex.Matches(h.RastroHost(), "apt-get").Count);
    }

    [Fact]
    public void Prevuelo_DnsNoCoincide_Aborta3AntesDeCertbot()
    {
        using var h = CrearHost();
        h.ResolverDominio(Dominio, "198.51.100.7");

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains(Dominio, r.Stderr);
        Assert.DoesNotContain("certbot", h.RastroHost());
        Assert.DoesNotContain("apt-get", h.RastroHost());
        SinSsh(h);
    }

    [Fact]
    public void Prevuelo_ElDominioNoResuelve_Aborta3()
    {
        using var h = HostSimulado.Crear();   // sin ResolverDominio: getent no lo conoce

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains(Dominio, r.Stderr);
        SinSsh(h);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prevuelo_VpsHostEsNombre_SeResuelveAntesDeComparar(bool vpsHostEsIp)
    {
        // VPS_HOST es un nombre (o una IP) y API_HOSTNAME otro nombre: el pre-vuelo resuelve los
        // dos y compara IPs, no cadenas. Resuelven a lo mismo -> sigue.
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        if (vpsHostEsIp) arbol.ConfigurarEnvNuevo(e => e["VPS_HOST"] = HostSimulado.IpDelHost);
        else Assert.NotEqual(Dominio, h.Host);

        var r = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*0/10\) .*OK", r.Stdout);
    }

    [Theory]
    [InlineData("9.9.9")]
    [InlineData("1.2")]
    public void Prevuelo_FaltaReleasesWinDeLaVersion_Aborta3(string version)
    {
        using var h = CrearHost();

        var r = Correr(h, ["--version", version, "--env-viejo", "{ENV_VIEJO}"]);

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("releases/win", r.Stderr);
        SinSsh(h);
    }

    [Fact]
    public void Prevuelo_SinCarpetaReleasesWin_Aborta3()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Directory.Delete(Path.Combine(arbol.Raiz, "releases"), recursive: true);

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("releases/win", r.Stderr);
        SinSsh(h);
    }

    [Theory]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64=CAMBIAR-pegar-la-clave-publica-base64-generada")]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64=CAMBIARpegarlaclavepublicabase64generada==")]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64=")]
    [InlineData("OTRA_CLAVE=abc")]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64=no*es*base64!")]
    [InlineData("LICENCIA_CLAVE_PUBLICA_BASE64=\"MCowBQYDK2VwAyEAabcdefghijklmnopqrstuvwxyz012345678=\"")]
    public void Prevuelo_ClavePublicaConCambiarOAusente_Aborta3(string contenido)
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        File.WriteAllText(arbol.EnvViejo, contenido + "\n");

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("LICENCIA_CLAVE_PUBLICA_BASE64", r.Stderr);
        // Los valores de la licencia nunca se imprimen: solo el nombre de la clave.
        var valor = contenido.Contains('=') ? contenido[(contenido.IndexOf('=') + 1)..].Trim('"') : "";
        if (valor.Length > 4)
        {
            Assert.DoesNotContain(valor, r.Stdout);
            Assert.DoesNotContain(valor, r.Stderr);
        }
        SinSsh(h);
    }

    [Fact]
    public void Prevuelo_EnvViejoInexistente_Aborta3()
    {
        using var h = CrearHost();

        var r = Correr(h, ["--version", Version, "--env-viejo", "/no/existe/env"]);

        Assert.True(r.Exit == 3, r.Todo);
        SinSsh(h);
    }

    // ---------- Pre-vuelo: alcance (A9) y consulta ----------

    [Fact]
    public void Prevuelo_ContenedorAjeno_Aborta9SinAcciones()
    {
        using var h = CrearHost();
        Assert.Equal(0, h.Ejecutar("sudo apt-get install -y docker.io docker-compose-v2").ExitCode);
        h.PlantarContenedorAjeno("pinar-web");
        var antes = h.RastroHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Contains("pinar-web", r.Stderr);
        var nuevas = string.Join('\n', LineasAdicionales(antes, h.RastroHost()));
        Assert.DoesNotContain("apt-get", nuevas);
        Assert.DoesNotContain("ufw", nuevas);
        Assert.NotEqual(0, h.Ejecutar("id stockapp").ExitCode);
    }

    [Fact]
    public void Prevuelo_ContenedoresDeStockapp_NoSonAjenos()
    {
        using var h = CrearHost();
        Assert.Equal(0, h.Ejecutar("sudo apt-get install -y docker.io docker-compose-v2").ExitCode);
        h.PlantarContenedorAjeno("stockapp-pg");

        var r = Correr(h, ConBase("--verificar"));

        Assert.NotEqual(9, r.Exit);
        Assert.True(r.Exit == 5, r.Todo);
    }

    [Fact]
    public void Prevuelo_Puerto80Ocupado_Aborta9SinAcciones()
    {
        using var h = CrearHost();
        h.OcuparPuerto80();
        var antes = h.RastroHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 9, r.Todo);
        Assert.Contains("80", r.Stderr);
        var nuevas = string.Join('\n', LineasAdicionales(antes, h.RastroHost()));
        Assert.DoesNotContain("apt-get", nuevas);
        Assert.DoesNotContain("ufw", nuevas);
        Assert.NotEqual(0, h.Ejecutar("id stockapp").ExitCode);
    }

    [Fact]
    public void NoPudeConsultar_SshSinRespuesta_Aborta8()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        // Nadie escucha en el 2299 y no hay conexión previa: el ssh sale 255.
        arbol.ConfigurarEnvNuevo(e => e["VPS_SSH_PORT"] = "2299");

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 8, r.Todo);
        Assert.DoesNotContain("YA ESTABA", r.Stdout);
        Assert.DoesNotContain("HECHO", r.Stdout);
    }

    // ---------- Pasos 1 y 2 ----------

    [Fact]
    public void Paso1Y2_HostEnBlanco_InstalanYCrean()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ OK", r.Stdout);
        Assert.Matches(@"\(\s*2/10\) usuario y directorios \.+ OK", r.Stdout);

        Assert.Contains("apt-get install -y docker.io docker-compose-v2", h.RastroHost());
        Assert.Contains(LineasSsh(h), l => l.Contains("usermod -aG docker " + HostSimulado.Usuario));
        var (_, grupos) = h.Ejecutar($"id -nG {HostSimulado.Usuario}");
        Assert.Contains("docker", grupos.Split(' ', '\n'));
        Assert.Equal(0, h.Ejecutar("docker ps").ExitCode);

        Assert.Equal(0, h.Ejecutar("id stockapp").ExitCode);
        var (_, passwd) = h.Ejecutar("getent passwd stockapp");
        Assert.Contains(":/var/lib/stockapp:/usr/sbin/nologin", passwd);

        var (exit, modos) = h.Ejecutar(
            "sudo stat -c '%U:%G %a %n' /var/lib/stockapp/.local /var/lib/stockapp/.local/share " +
            "/var/lib/stockapp/.local/share/StockApp ~/stockapp-deploy ~/stockapp-deploy/migracion");
        Assert.True(exit == 0, modos);
        var lineas = modos.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("stockapp:stockapp 750 /var/lib/stockapp/.local", lineas[0]);
        Assert.Equal("stockapp:stockapp 750 /var/lib/stockapp/.local/share", lineas[1]);
        Assert.Equal("stockapp:stockapp 750 /var/lib/stockapp/.local/share/StockApp", lineas[2]);
        Assert.Equal($"{HostSimulado.Usuario}:{HostSimulado.Usuario} 700 /home/{HostSimulado.Usuario}/stockapp-deploy", lineas[3]);
        Assert.Equal($"{HostSimulado.Usuario}:{HostSimulado.Usuario} 700 /home/{HostSimulado.Usuario}/stockapp-deploy/migracion", lineas[4]);
    }

    [Fact]
    public void Paso1Y2_ReCorrer_DiceYaEstabaYNoAplicaNada()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        var primera = arbol.Correr(ConBase());
        Assert.True(primera.Exit == 0, primera.Todo);
        var hostAntes = h.RastroHost();

        var segunda = arbol.Correr(ConBase());

        Assert.True(segunda.Exit == 0, segunda.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ YA ESTABA", segunda.Stdout);
        Assert.Matches(@"\(\s*2/10\) usuario y directorios \.+ YA ESTABA", segunda.Stdout);
        Assert.DoesNotContain("apt-get", string.Join('\n', LineasAdicionales(hostAntes, h.RastroHost())));
        // El usermod de la primera corrida es el único: la segunda no volvió a aplicar el paso.
        Assert.Single(LineasSsh(h).Where(l => l.Contains("usermod")));
    }

    [Fact]
    public void Paso1_VerificaConUnaConexionNueva()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        var lineas = LineasSsh(h);
        var iUsermod = Array.FindIndex(lineas, l => l.Contains("usermod"));
        Assert.True(iUsermod >= 0, "no hay un ssh con usermod:\n" + string.Join('\n', lineas));
        var despues = lineas[(iUsermod + 1)..];
        var iDocker = Array.FindIndex(despues, l => l.Contains("docker ps"));
        Assert.True(iDocker >= 0, "no hay un ssh con `docker ps` después del usermod:\n" + string.Join('\n', lineas));
        // Otra invocación de ssh, y sin reutilizar la conexión que ya estaba abierta.
        Assert.NotEqual(lineas[iUsermod], despues[iDocker]);
        Assert.Contains("ControlMaster=no", despues[iDocker]);
        Assert.DoesNotContain("sudo", despues[iDocker]);
    }

    [Fact]
    public void Paso1_AptFalla_Aborta5YNoSigueAlPaso2()
    {
        using var h = CrearHost();
        h.Escribir("/usr/local/sbin/apt-get", "#!/bin/bash\necho 'E: simulado' >&2\nexit 100\n");
        Assert.Equal(0, h.Ejecutar("sudo chmod 0755 /usr/local/sbin/apt-get").ExitCode);

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ FALLÓ", r.Stdout);
        Assert.DoesNotMatch(@"\(\s*2/10\)", r.Stdout);
        Assert.NotEqual(0, h.Ejecutar("id stockapp").ExitCode);
    }

    [Fact]
    public void Paso1_ReCorrerTrasUnaFalla_Continua()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        h.Escribir("/usr/local/sbin/apt-get", "#!/bin/bash\nexit 100\n");
        Assert.Equal(0, h.Ejecutar("sudo chmod 0755 /usr/local/sbin/apt-get").ExitCode);
        Assert.Equal(5, arbol.Correr(ConBase()).Exit);
        // Se arregla el problema: el apt-get de verdad (el shim) vuelve a su lugar.
        Assert.Equal(0, h.Ejecutar("sudo install -m 0755 /opt/hostsim-src/apt-get /usr/local/sbin/apt-get").ExitCode);

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ OK", r.Stdout);
        Assert.Matches(@"\(\s*2/10\) usuario y directorios \.+ OK", r.Stdout);
    }

    [Fact]
    public void Verificar_HostEnBlanco_TodoPendienteSinAcciones()
    {
        using var h = CrearHost();
        var antes = h.RastroHost();

        var r = Correr(h, ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ PENDIENTE", r.Stdout);
        Assert.Matches(@"\(\s*2/10\) usuario y directorios \.+ PENDIENTE", r.Stdout);
        Assert.DoesNotContain("YA ESTABA", r.Stdout);

        // Solo lectura: nada instalado, nada creado, nada copiado.
        Assert.NotEqual(0, h.Ejecutar("command -v docker").ExitCode);
        Assert.NotEqual(0, h.Ejecutar("id stockapp").ExitCode);
        Assert.NotEqual(0, h.Ejecutar("test -d ~/stockapp-deploy").ExitCode);
        Assert.DoesNotContain("apt-get", string.Join('\n', LineasAdicionales(antes, h.RastroHost())));
        Assert.DoesNotContain("SCP-ARGS:", h.Rastro());
        Assert.DoesNotContain(LineasSsh(h), l => l.Contains("usermod") || l.Contains("apt-get") || l.Contains("useradd"));
    }

    [Fact]
    public void Verificar_TrasPreparar_Exit0TodoHecho()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        var antes = h.RastroHost();

        var r = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ HECHO", r.Stdout);
        Assert.Matches(@"\(\s*2/10\) usuario y directorios \.+ HECHO", r.Stdout);
        Assert.DoesNotContain("PENDIENTE", r.Stdout);
        Assert.DoesNotContain("apt-get", string.Join('\n', LineasAdicionales(antes, h.RastroHost())));
    }

    [Fact]
    public void Verificar_UnPasoRotoTrasPreparar_Exit5()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        Assert.Equal(0, h.Ejecutar("sudo chmod 755 /var/lib/stockapp/.local/share").ExitCode);

        var r = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ HECHO", r.Stdout);
        Assert.Matches(@"\(\s*2/10\) usuario y directorios \.+ PENDIENTE", r.Stdout);
        Assert.Equal("755", h.Ejecutar("sudo stat -c %a /var/lib/stockapp/.local/share").Salida.Trim());
    }

    // ---------- Paso 3: firewall con reversa (A8) ----------

    private const string Reversa = "stockapp-ufw-reversa";
    private const string TimerDeLaReversa = "/run/systemd/transient/stockapp-ufw-reversa.timer";

    private static bool TimerArmado(HostSimulado h) =>
        h.Ejecutar($"test -f {TimerDeLaReversa}").ExitCode == 0;

    /// <summary>
    /// Reemplaza un shim del host por un envoltorio que, ante los argumentos dados, hace como
    /// que anduvo (exit 0, sin tocar nada) y, ante cualquier otra cosa, delega en el shim
    /// original (movido a `.real`). Sirve para simular un ufw que ignora una regla, un
    /// systemd-run que no arma nada o un systemctl que no para un timer.
    /// </summary>
    private static void Envolver(HostSimulado h, string ruta, string patronDeArgumentos)
    {
        Assert.Equal(0, h.Ejecutar($"sudo mv {ruta} {ruta}.real").ExitCode);
        h.Escribir(ruta,
            "#!/bin/bash\n" +
            $"case \"$*\" in\n  {patronDeArgumentos}) exit 0 ;;\nesac\n" +
            $"exec {ruta}.real \"$@\"\n");
        Assert.Equal(0, h.Ejecutar($"sudo chmod 0755 {ruta}").ExitCode);
    }

    private static void Desenvolver(HostSimulado h, string ruta) =>
        Assert.Equal(0, h.Ejecutar($"sudo mv {ruta}.real {ruta}").ExitCode);

    private static int Indice(string[] lineas, int desde, Func<string, bool> condicion) =>
        Array.FindIndex(lineas, desde, l => condicion(l));

    [Fact]
    public void Firewall_Orden()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*3/10\) firewall \.+ OK", r.Stdout);
        var l = LineasSsh(h);
        var texto = string.Join('\n', l);
        var iAllowSsh = Indice(l, 0, x => x.Contains($"ufw allow {h.PuertoSsh}/tcp"));
        var iAllow80 = Indice(l, 0, x => x.Contains("ufw allow 80/tcp"));
        var iAllowApi = Indice(l, 0, x => x.Contains("ufw allow 8443/tcp"));
        var iReversa = Indice(l, 0, x => x.Contains($"systemd-run --unit={Reversa}"));
        var iEnable = Indice(l, 0, x => x.Contains("ufw --force enable"));
        var iNueva = iEnable < 0 ? -1 : Indice(l, iEnable + 1, x => x.Contains("sudo -n true") && x.Contains("ControlMaster=no"));
        var iStop = iNueva < 0 ? -1 : Indice(l, iNueva + 1, x => x.Contains($"systemctl stop {Reversa}.timer"));

        Assert.True(iAllowSsh >= 0 && iAllow80 >= 0 && iAllowApi >= 0, "faltan los tres ufw allow:\n" + texto);
        Assert.True(iReversa >= 0, "no se armó la reversa:\n" + texto);
        Assert.True(iEnable >= 0, "no hay ufw --force enable:\n" + texto);
        Assert.True(iNueva >= 0, "no hay una conexión nueva después del enable:\n" + texto);
        Assert.True(iStop >= 0, "no se desarmó la reversa después de la conexión nueva:\n" + texto);
        // El orden exacto: los tres allow < la reversa < el enable < la conexión nueva < el stop.
        Assert.True(Math.Max(iAllowSsh, Math.Max(iAllow80, iAllowApi)) < iReversa, texto);
        Assert.True(iReversa < iEnable, texto);
        Assert.Contains("--on-active=120", l[iReversa]);
        Assert.Contains("/usr/sbin/ufw disable", l[iReversa]);
        // Con una conexión que no sale del cliente: sin maestro, sin esperar de más, sin preguntar.
        Assert.Contains("ControlPath=none", l[iNueva]);
        Assert.Contains("ConnectTimeout=", l[iNueva]);
        Assert.Contains("BatchMode=yes", l[iNueva]);

        // El resultado: firewall activo con las tres reglas y ninguna reversa pendiente.
        var estado = h.EstadoUfw();
        Assert.Contains("Status: active", estado);
        foreach (var regla in new[] { $"{h.PuertoSsh}/tcp", "80/tcp", "8443/tcp" })
            Assert.Matches($@"(?m)^{Regex.Escape(regla)}\s+ALLOW\s+Anywhere$", estado);
        Assert.False(TimerArmado(h), "la reversa quedó armada");
    }

    [Fact]
    public void Firewall_PuertoSshDelEnv_NoElDe22()
    {
        using var h = HostSimulado.Crear(puertoSsh: 2222);
        h.ResolverDominio(Dominio);

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Contains("ufw allow 2222/tcp", string.Join('\n', LineasSsh(h)));
        Assert.Matches(@"(?m)^2222/tcp\s+ALLOW\s+Anywhere$", h.EstadoUfw());
    }

    [Fact]
    public void Firewall_NuncaReset()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        var todo = string.Join('\n', LineasSsh(h)) + "\n" + h.RastroHost();
        Assert.DoesNotContain("ufw reset", todo);
        Assert.DoesNotContain("ufw delete", todo);
        Assert.DoesNotContain("--force reset", todo);
    }

    [Fact]
    public void Firewall_ReChequeoFallido_Sale6ConLaReversaArmada()
    {
        using var h = CrearHost();
        // Un ufw que "acepta" la regla del puerto ssh y no la crea: el caso del puerto mal tipeado.
        Envolver(h, "/usr/sbin/ufw", $"\"allow {h.PuertoSsh}/tcp\"");

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 6, r.Todo);
        Assert.Contains("la reversa deshace el firewall en 120 s; esperá y re-corré", r.Stderr);
        Assert.Matches(@"\(\s*3/10\) firewall \.+ FALLÓ", r.Stdout);
        Assert.True(TimerArmado(h), "la reversa no está armada y el operador quedó afuera");
        Assert.DoesNotContain(LineasSsh(h), x => x.Contains($"systemctl stop {Reversa}"));
        Assert.Contains("Status: active", h.EstadoUfw());
        // Nada de lo que sigue al firewall corrió.
        Assert.DoesNotMatch(@"\(\s*4/10\)", r.Stdout);
    }

    [Fact]
    public void Firewall_ReChequeo_NoReusaElMaestroSsh()
    {
        using var h = CrearHost();
        Envolver(h, "/usr/sbin/ufw", $"\"allow {h.PuertoSsh}/tcp\"");

        var r = Correr(h, ConBase());

        // El maestro ya estaba abierto (lo abrió el pre-vuelo): una conexión que lo reutilizara
        // diría "anda" con el firewall cerrado y desarmaría la reversa. Tiene que salir 6.
        Assert.True(File.Exists(Path.Combine(h.DirOperador, $"maestro-{HostSimulado.Usuario}-{h.PuertoSsh}")),
            "el test no abrió el maestro: no prueba nada");
        Assert.True(r.Exit == 6, r.Todo);
        var l = LineasSsh(h);
        var iEnable = Indice(l, 0, x => x.Contains("ufw --force enable"));
        Assert.True(iEnable >= 0, string.Join('\n', l));
        var recheck = l[(iEnable + 1)..].Where(x => x.Contains("sudo -n true")).ToArray();
        Assert.NotEmpty(recheck);
        Assert.All(recheck, x =>
        {
            Assert.Contains("ControlMaster=no", x);
            Assert.Contains("ControlPath=none", x);
        });
        Assert.True(TimerArmado(h));
    }

    [Fact]
    public void Firewall_TrasLaReversa_ReCorrerCompleta()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Envolver(h, "/usr/sbin/ufw", $"\"allow {h.PuertoSsh}/tcp\"");
        Assert.Equal(6, arbol.Correr(ConBase()).Exit);
        // El timer vence: la reversa corre `ufw disable` y la unidad transitoria se borra.
        Assert.Equal(0, h.Ejecutar($"sudo systemctl start {Reversa}.service").ExitCode);
        Assert.DoesNotContain("Status: active", h.EstadoUfw());
        Assert.False(TimerArmado(h));
        Desenvolver(h, "/usr/sbin/ufw");

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*3/10\) firewall \.+ OK", r.Stdout);
        Assert.Contains("Status: active", h.EstadoUfw());
        Assert.False(TimerArmado(h));
    }

    [Fact]
    public void Firewall_YaActivo_NoReArmaNiReHabilita()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        var sshAntes = LineasSsh(h).Length;
        var hostAntes = h.RastroHost();

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*3/10\) firewall \.+ YA ESTABA", r.Stdout);
        var nuevas = string.Join('\n', LineasSsh(h)[sshAntes..]) + "\n" + string.Join('\n', LineasAdicionales(hostAntes, h.RastroHost()));
        Assert.DoesNotContain("systemd-run", nuevas);
        Assert.DoesNotContain("enable", nuevas);
        Assert.DoesNotContain("ufw allow", nuevas);
    }

    [Fact]
    public void Firewall_ActivoSinUnaRegla_LaAgregaSinReArmarNiReHabilitar()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        Assert.Equal(0, h.Ejecutar("sudo sed -i '/^8443\\/tcp$/d' /var/lib/hostsim/ufw/reglas").ExitCode);
        Assert.DoesNotMatch(@"(?m)^8443/tcp", h.EstadoUfw());
        var sshAntes = LineasSsh(h).Length;

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*3/10\) firewall \.+ OK", r.Stdout);
        Assert.Matches(@"(?m)^8443/tcp\s+ALLOW\s+Anywhere$", h.EstadoUfw());
        var nuevas = string.Join('\n', LineasSsh(h)[sshAntes..]);
        Assert.DoesNotContain("systemd-run", nuevas);
        Assert.DoesNotContain("ufw --force enable", nuevas);
    }

    [Fact]
    public void Firewall_ReversaYaArmada_NoReArmaYLaDesarmaTrasVerificar()
    {
        // Una corrida anterior que murió con la reversa armada y el firewall todavía activo.
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        Assert.Equal(0, h.Ejecutar($"sudo systemd-run --unit={Reversa} --on-active=120 /usr/sbin/ufw disable").ExitCode);
        Assert.True(TimerArmado(h));
        var sshAntes = LineasSsh(h).Length;

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.DoesNotMatch(@"\(\s*3/10\) firewall \.+ YA ESTABA", r.Stdout);   // con la reversa armada NO está hecho
        var nuevas = LineasSsh(h)[sshAntes..];
        Assert.DoesNotContain(nuevas, x => x.Contains("systemd-run"));
        Assert.DoesNotContain(nuevas, x => x.Contains("ufw --force enable"));
        var iNueva = Indice(nuevas, 0, x => x.Contains("sudo -n true") && x.Contains("ControlMaster=no"));
        var iStop = Indice(nuevas, 0, x => x.Contains($"systemctl stop {Reversa}.timer"));
        Assert.True(iNueva >= 0 && iStop > iNueva, string.Join('\n', nuevas));
        Assert.False(TimerArmado(h));
        Assert.Contains("Status: active", h.EstadoUfw());
    }

    [Fact]
    public void Firewall_InactivoConReversaVieja_ParaLaViejaYArmaUnaNuevaAntesDelEnable()
    {
        // Una corrida anterior murió en el enable: el firewall volvió a inactivo y la reversa
        // quedó armada con un plazo desconocido. Reusarla puede dejar el enable sin red.
        using var h = CrearHost();
        Assert.Equal(0, h.Ejecutar($"sudo systemd-run --unit={Reversa} --on-active=120 /usr/sbin/ufw disable").ExitCode);
        Assert.True(TimerArmado(h));
        Assert.DoesNotContain("Status: active", h.EstadoUfw());

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        var l = LineasSsh(h);
        var texto = string.Join('\n', l);
        var iAllowSsh = Indice(l, 0, x => x.Contains($"ufw allow {h.PuertoSsh}/tcp"));
        var iStopViejo = Indice(l, iAllowSsh + 1, x => x.Contains($"systemctl stop {Reversa}.timer"));
        var iNueva = Indice(l, iStopViejo + 1, x => x.Contains($"systemd-run --unit={Reversa}"));
        var iEnable = Indice(l, iNueva + 1, x => x.Contains("ufw --force enable"));
        Assert.True(iStopViejo > iAllowSsh && iNueva > iStopViejo && iEnable > iNueva,
            "esperado: stop de la vieja < systemd-run nuevo < enable:\n" + texto);
        // La vieja se confirmó desaparecida antes de armar la nueva.
        Assert.Contains(l[(iStopViejo + 1)..iNueva], x => x.Contains("show -p LoadState"));
        // Y la nueva se desarmó después del re-chequeo.
        Assert.Contains(l[(iEnable + 1)..], x => x.Contains($"systemctl stop {Reversa}.timer"));
        Assert.False(TimerArmado(h));
        Assert.Contains("Status: active", h.EstadoUfw());
    }

    [Fact]
    public void Firewall_InactivoConReversaVieja_SiNoSePuedeParar_NoHabilita()
    {
        using var h = CrearHost();
        Assert.Equal(0, h.Ejecutar($"sudo systemd-run --unit={Reversa} --on-active=120 /usr/sbin/ufw disable").ExitCode);
        Envolver(h, "/usr/local/sbin/systemctl", $"\"stop {Reversa}.timer\"");

        var r = Correr(h, ConBase());

        Assert.NotEqual(0, r.Exit);
        Assert.DoesNotContain("ufw --force enable", string.Join('\n', LineasSsh(h)) + h.RastroHost());
        Assert.DoesNotContain("Status: active", h.EstadoUfw());
        Assert.DoesNotMatch(@"\(\s*4/10\)", r.Stdout);
    }

    [Fact]
    public void Firewall_ElStopNoDesarmaLaReversa_Sale6()
    {
        using var h = CrearHost();
        // Un systemctl que dice que paró el timer y no lo paró: el exit 0 no alcanza.
        Envolver(h, "/usr/local/sbin/systemctl", $"\"stop {Reversa}.timer\"");

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 6, r.Todo);
        Assert.Contains("la reversa deshace el firewall en 120 s; esperá y re-corré", r.Stderr);
        Assert.True(TimerArmado(h));
    }

    [Fact]
    public void Firewall_LaReversaNoQuedaArmada_NoHabilitaElFirewall()
    {
        using var h = CrearHost();
        // Un systemd-run que sale 0 sin crear el timer: habilitar sin red de seguridad es el riesgo.
        Envolver(h, "/usr/local/sbin/systemd-run", "*");

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 5, r.Todo);
        Assert.DoesNotContain("ufw --force enable", string.Join('\n', LineasSsh(h)) + h.RastroHost());
        Assert.DoesNotContain("Status: active", h.EstadoUfw());
        Assert.DoesNotMatch(@"\(\s*4/10\)", r.Stdout);
    }

    [Fact]
    public void Verificar_FirewallSinActivar_PendienteYSoloLee()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*3/10\) firewall \.+ PENDIENTE", r.Stdout);
        var todo = string.Join('\n', LineasSsh(h)) + "\n" + h.RastroHost();
        Assert.DoesNotContain("ufw allow", todo);
        Assert.DoesNotContain("systemd-run", todo);
        Assert.DoesNotContain("enable", todo);
    }

    // ---------- Paso 4: TLS (certbot + deploy-hook) ----------

    private const string HookInstalado = "/usr/local/sbin/stockapp-tls-deploy-hook.sh";

    private static string[] LineasHost(HostSimulado h) =>
        h.RastroHost().Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static int Cuantas(string texto, string fragmento) =>
        Regex.Matches(texto, Regex.Escape(fragmento)).Count;

    /// <summary>Planta la unit de la API y la arranca (lo que dejaría el primer deploy).</summary>
    private static void PlantarApiActiva(HostSimulado h)
    {
        h.Escribir("/etc/systemd/system/stockapp-api.service", "[Service]\nExecStart=/bin/true\n");
        Assert.Equal(0, h.Ejecutar("sudo systemctl start stockapp-api").ExitCode);
    }

    [Fact]
    public void Tls_CertbotSaleDistintoDeCeroPeroCopio_Ok()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        // La API todavía no existe: el hook copia los PEM y su `systemctl restart` falla, así
        // que certbot sale distinto de cero aunque emitió. El paso mira el RESULTADO.
        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ OK", r.Stdout);
        var host = h.RastroHost();
        Assert.Contains(
            $"certbot certonly --standalone -n --agree-tos --register-unsafely-without-email -d {Dominio} --deploy-hook {HookInstalado}",
            host);
        Assert.Contains("systemctl restart stockapp-api", host);   // el hook corrió y su restart falló
        Assert.Equal("stockapp:stockapp 600", h.Ejecutar("sudo stat -c '%U:%G %a' /var/lib/stockapp/tls/fullchain.pem").Salida.Trim());
        Assert.Equal("stockapp:stockapp 600", h.Ejecutar("sudo stat -c '%U:%G %a' /var/lib/stockapp/tls/privkey.pem").Salida.Trim());
        Assert.Contains($"renew_hook = {HookInstalado}", h.Leer($"/etc/letsencrypt/renewal/{Dominio}.conf"));
    }

    [Fact]
    public void Tls_ElHookSeInstalaPorScpYConInstallRootRoot0755()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Contains("SCP-ARGS:", h.Rastro());
        Assert.Contains("stockapp-deploy/stockapp-tls-deploy-hook.sh", h.Rastro());
        Assert.Equal("root:root 755", h.Ejecutar($"sudo stat -c '%U:%G %a' {HookInstalado}").Salida.Trim());
        Assert.Equal(
            File.ReadAllText(Path.Combine(BashLimpio.RaizRepo(), "deploy", "tls", "stockapp-tls-deploy-hook.sh")).TrimEnd(),
            h.Leer(HookInstalado).TrimEnd());
        var l = LineasSsh(h);
        Assert.Contains(l, x => x.Contains("install -m 0755 -o root -g root") && x.Contains(HookInstalado));
    }

    [Fact]
    public void Tls_EmitioPeroNoCopio_CorreElHookAMano()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        // El hook de la primera invocación (la de certbot) sale 1 sin copiar nada; las demás
        // son el hook real. Así certbot emite pero los PEM no quedan.
        var real = File.ReadAllText(Path.Combine(arbol.Raiz, "deploy", "tls", "stockapp-tls-deploy-hook.sh"));
        File.WriteAllText(Path.Combine(arbol.Raiz, "deploy", "tls", "stockapp-tls-deploy-hook.sh"),
            "#!/usr/bin/env bash\n" +
            "if [[ ! -e /var/lib/hostsim/hook-primera-vez ]]; then : > /var/lib/hostsim/hook-primera-vez; exit 1; fi\n" +
            real.Replace("#!/usr/bin/env bash\n", ""));

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ OK", r.Stdout);
        Assert.Equal(1, Cuantas(h.RastroHost(), "certbot certonly"));
        Assert.Contains(LineasSsh(h),
            l => l.Contains($"RENEWED_LINEAGE=/etc/letsencrypt/live/{Dominio}") && l.Contains(HookInstalado));
        Assert.Equal("stockapp:stockapp 600", h.Ejecutar("sudo stat -c '%U:%G %a' /var/lib/stockapp/tls/privkey.pem").Salida.Trim());
    }

    [Fact]
    public void Tls_Vigente_NoReEmite()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        var rastroAntes = h.Rastro();
        var hostAntes = h.RastroHost();

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ YA ESTABA", r.Stdout);
        var nuevas = string.Join('\n', LineasAdicionales(hostAntes, h.RastroHost()));
        Assert.DoesNotContain("certbot", nuevas);
        Assert.DoesNotContain("apt-get", nuevas);
        Assert.Equal(1, Cuantas(h.RastroHost(), "certbot certonly"));
        Assert.DoesNotContain("SCP-ARGS:", string.Join('\n', LineasAdicionales(rastroAntes, h.Rastro())));
    }

    [Fact]
    public void Tls_UsuarioCreadoAntesDelCertificado()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        // El hook deja constancia de si `stockapp` existía cuando certbot lo corrió (hace chown).
        var ruta = Path.Combine(arbol.Raiz, "deploy", "tls", "stockapp-tls-deploy-hook.sh");
        File.WriteAllText(ruta, File.ReadAllText(ruta).Replace("set -euo pipefail\n",
            "set -euo pipefail\nif id stockapp >/dev/null 2>&1; then echo si > /var/lib/hostsim/usuario-al-emitir; fi\n"));

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Equal("si", h.Leer("/var/lib/hostsim/usuario-al-emitir").Trim());
        var l = LineasSsh(h);
        var iUsuario = Array.FindIndex(l, x => x.Contains("sudo -n bash -s -- " + HostSimulado.Usuario));
        var iCertonly = Array.FindIndex(l, x => x.Contains("certbot certonly"));
        Assert.True(iUsuario >= 0 && iCertonly > iUsuario, string.Join('\n', l));
    }

    [Fact]
    public void Tls_ConElFirewallActivo_LaReglaDelPuerto80DejaPasarALetsEncrypt()
    {
        using var h = CrearHost();

        var r = Correr(h, ConBase());

        // El certbot falso falla con `Timeout during connect (likely firewall problem)` si ufw
        // está activo y no tiene la regla del 80: el paso 3 tiene que dejarla antes del 4.
        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"(?m)^80/tcp\s+ALLOW\s+Anywhere$", h.EstadoUfw());
        var l = LineasHost(h);
        Assert.True(Array.FindIndex(l, x => x.StartsWith("ufw --force enable"))
                    < Array.FindIndex(l, x => x.StartsWith("certbot certonly")), string.Join('\n', l));
        Assert.Equal(0, h.Ejecutar($"sudo test -e /etc/letsencrypt/live/{Dominio}/fullchain.pem").ExitCode);
    }

    [Fact]
    public void Tls_CertbotQueNoEmite_Aborta5NombrandoElPaso()
    {
        using var h = CrearHost();
        Assert.Equal(0, h.Ejecutar("sudo apt-get install -y certbot").ExitCode);
        Envolver(h, "/usr/local/sbin/certbot", "*certonly*");   // "anda" (exit 0) y no emite nada

        var r = Correr(h, ConBase());

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ FALLÓ", r.Stdout);
        Assert.NotEqual(0, h.Ejecutar($"sudo test -e /etc/letsencrypt/live/{Dominio}").ExitCode);
    }

    [Fact]
    public void Tls_CertificadoPorVencer_NoCuentaComoHecho()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        // Un certificado que vence en 10 días (menos de los 30 exigidos), en el lugar del vigente.
        var tmp = Path.Combine(Path.GetTempPath(), "stockapp-corto-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(tmp);
        try
        {
            var (exitSsl, _, errSsl) = BashLimpio.Ejecutar(
                $"openssl req -x509 -days 10 -newkey ec -pkeyopt ec_paramgen_curve:prime256v1 -nodes " +
                $"-subj /CN=corto.test.invalid -keyout {Sq(tmp)}/k.pem -out {Sq(tmp)}/c.pem");
            Assert.True(exitSsl == 0, errSsl);
            h.Escribir($"/etc/letsencrypt/archive/{Dominio}/fullchain1.pem", File.ReadAllText(Path.Combine(tmp, "c.pem")));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }

        var r = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ PENDIENTE", r.Stdout);
    }

    [Fact]
    public void Tls_RenewHookDistinto_NoCuentaComoHecho()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        Assert.Equal(0, h.Ejecutar(
            $"sudo sed -i 's|^renew_hook = .*|renew_hook = /usr/local/sbin/otro-hook.sh|' /etc/letsencrypt/renewal/{Dominio}.conf").ExitCode);

        var r = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ PENDIENTE", r.Stdout);
    }

    [Fact]
    public void Tls_CertVigenteConOtroRenewHook_ReconfiguraYQuedaHecho_SinReEmitir()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        Assert.Equal(0, h.Ejecutar(
            $"sudo sed -i 's|^renew_hook = .*|renew_hook = /usr/local/sbin/otro-hook.sh|' /etc/letsencrypt/renewal/{Dominio}.conf").ExitCode);

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ OK", r.Stdout);
        Assert.Contains($"certbot reconfigure --cert-name {Dominio} --deploy-hook {HookInstalado}", h.RastroHost());
        Assert.Equal(1, Cuantas(h.RastroHost(), "certbot certonly"));
        Assert.Contains($"renew_hook = {HookInstalado}", h.Leer($"/etc/letsencrypt/renewal/{Dominio}.conf"));
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ HECHO", arbol.Correr(ConBase("--verificar")).Stdout);
    }

    [Fact]
    public void Tls_PemConModoDistinto_SeCorrigeCorriendoElHook_SinReEmitir()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        Assert.Equal(0, arbol.Correr(ConBase()).Exit);
        Assert.Equal(0, h.Ejecutar("sudo chmod 644 /var/lib/stockapp/tls/privkey.pem").ExitCode);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ PENDIENTE", arbol.Correr(ConBase("--verificar")).Stdout);

        var r = arbol.Correr(ConBase());

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*4/10\) certificado TLS \.+ OK", r.Stdout);
        Assert.Equal(1, Cuantas(h.RastroHost(), "certbot certonly"));
        Assert.Equal("stockapp:stockapp 600", h.Ejecutar("sudo stat -c '%U:%G %a' /var/lib/stockapp/tls/privkey.pem").Salida.Trim());
    }

    // ---------- Paso 9: renovación (solo con --probar-renovacion) ----------

    private const string Marcador = "stockapp-deploy/.preparacion/renovacion-probada";

    [Fact]
    public void Renovacion_SinFlag_NuncaForceRenewal()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);

        var r = arbol.Correr(ConBase());
        var verificar = arbol.Correr(ConBase("--verificar"));

        Assert.True(r.Exit == 0, r.Todo);
        Assert.Matches(@"\(\s*9/10\) renovación del certificado \.+ OMITIDO.*\(sin --probar-renovacion\)", r.Stdout);
        Assert.True(verificar.Exit == 0, verificar.Todo);   // el 9 no decide el exit de --verificar
        Assert.Matches(@"\(\s*9/10\) renovación del certificado \.+ OMITIDO", verificar.Stdout);
        Assert.DoesNotContain("--force-renewal", h.RastroHost());
        Assert.DoesNotContain("--force-renewal", h.Rastro());
        Assert.DoesNotContain("--force-renewal", r.Stdout + r.Stderr);
        Assert.NotEqual(0, h.Ejecutar($"test -e ~/{Marcador}").ExitCode);
    }

    [Fact]
    public void Renovacion_ConFlag_UnaSolaVez()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        PlantarApiActiva(h);

        var primera = arbol.Correr(ConBase("--probar-renovacion"));

        Assert.True(primera.Exit == 0, primera.Todo);
        Assert.Matches(@"\(\s*9/10\) renovación del certificado \.+ OK", primera.Stdout);
        Assert.Equal(1, Cuantas(h.RastroHost(), "--force-renewal"));
        Assert.Contains($"certbot renew --force-renewal --cert-name {Dominio}", h.RastroHost());
        var (exit, contenido) = h.Ejecutar($"cat ~/{Marcador}");
        Assert.True(exit == 0, contenido);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}", contenido.Trim());
        Assert.Equal($"{HostSimulado.Usuario} 700",
            h.Ejecutar("stat -c '%U %a' ~/stockapp-deploy/.preparacion").Salida.Trim());

        var segunda = arbol.Correr(ConBase("--probar-renovacion"));

        Assert.True(segunda.Exit == 0, segunda.Todo);
        Assert.Matches(@"\(\s*9/10\) renovación del certificado \.+ YA ESTABA", segunda.Stdout);
        Assert.Equal(1, Cuantas(h.RastroHost(), "--force-renewal"));
    }

    [Fact]
    public void Renovacion_VerificaArranquePosterior()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        PlantarApiActiva(h);
        var arranquesAntes = Cuantas(h.RastroHost(), "systemctl restart stockapp-api");

        var r = arbol.Correr(ConBase("--probar-renovacion"));

        // El hook (que corre dentro de `certbot renew`) reinició la API después de renovar, y
        // el paso lo comprobó mirando el journal además del is-active.
        Assert.True(r.Exit == 0, r.Todo);
        var l = LineasHost(h);
        var iRenew = Array.FindIndex(l, x => x.StartsWith("certbot renew"));
        var iRestart = Array.FindIndex(l, iRenew + 1, x => x.StartsWith("systemctl restart stockapp-api"));
        Assert.True(iRenew >= 0 && iRestart > iRenew, string.Join('\n', l));
        Assert.True(Cuantas(h.RastroHost(), "systemctl restart stockapp-api") > arranquesAntes);
        Assert.Contains(l, x => x.StartsWith("journalctl") && x.Contains("stockapp-api"));
    }

    [Fact]
    public void Renovacion_SinArranquePosterior_FallaYNoDejaMarcador()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);
        PlantarApiActiva(h);
        // Un hook que copia pero NO reinicia: la API sigue `active` (del arranque viejo), pero no
        // hubo un arranque posterior a la renovación.
        var ruta = Path.Combine(arbol.Raiz, "deploy", "tls", "stockapp-tls-deploy-hook.sh");
        File.WriteAllText(ruta, File.ReadAllText(ruta).Replace("systemctl restart stockapp-api", "true"));

        var r = arbol.Correr(ConBase("--probar-renovacion"));

        Assert.True(r.Exit == 5, r.Todo);
        Assert.Matches(@"\(\s*9/10\) renovación del certificado \.+ FALLÓ", r.Stdout);
        Assert.Equal("active", h.Ejecutar("systemctl is-active stockapp-api").Salida.Trim());
        Assert.NotEqual(0, h.Ejecutar($"test -e ~/{Marcador}").ExitCode);
    }

    // ---------- Lock ----------

    [Fact]
    public void Verificar_ConElLockTomadoPorElPadre_NoLoToma()
    {
        // migrar tiene el flock y llama a `preparar --verificar` como bloque (fd 9 cerrado).
        using var h = CrearHost();
        using var arbol = new Arbol(h);

        var r = arbol.Correr(ConBase("--verificar"), conLockTomado: true);

        Assert.True(r.Exit == 5, r.Todo);   // host en blanco: pasos 1-2 PENDIENTE, no 3 por el lock
        Assert.DoesNotContain("migración en curso", r.Stderr);
        Assert.Matches(@"\(\s*1/10\) Docker \.+ PENDIENTE", r.Stdout);
    }

    [Fact]
    public void ModoNormal_ConElLockTomado_Aborta3SinSsh()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);

        var r = arbol.Correr(ConBase(), conLockTomado: true);

        Assert.True(r.Exit == 3, r.Todo);
        Assert.Contains("migración en curso", r.Stderr);
        SinSsh(h);
    }

    // ---------- Los bloques falsos del árbol ----------

    [Fact]
    public void BloquesFalsos_RegistranArgsYEnvYLaUnitQuedaEnElHost()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);

        var (exit, _, stderr) = h.Operador(
            $"export HS_DIR={Sq(h.DirOperador)} STOCKAPP_ENV_LOCAL=/ruta/env\n" +
            $"{Sq(Path.Combine(arbol.Raiz, "deploy", "deploy-vps.sh"))} {Version} --sin-backup\n" +
            $"{Sq(Path.Combine(arbol.Raiz, "build", "publicar-release.sh"))} {Version}");

        Assert.True(exit == 0, stderr);
        var rastro = h.Rastro();
        Assert.Contains($"BLOQUE deploy-vps.sh {Version} --sin-backup ENV=/ruta/env", rastro);
        Assert.Contains($"BLOQUE publicar-release.sh {Version} ENV=/ruta/env", rastro);
        Assert.Equal(0, h.Ejecutar("test -f /etc/systemd/system/stockapp-api.service").ExitCode);
    }

    [Fact]
    public void BloquesFalsos_SalenConElCodigoPedidoYNoCreanLaUnit()
    {
        using var h = CrearHost();
        using var arbol = new Arbol(h);

        var (exitDeploy, _, _) = h.Operador(
            $"export HS_DIR={Sq(h.DirOperador)} STUB_DEPLOY_EXIT=7\n{Sq(Path.Combine(arbol.Raiz, "deploy", "deploy-vps.sh"))} {Version}");
        var (exitPublicar, _, _) = h.Operador(
            $"export HS_DIR={Sq(h.DirOperador)} STUB_PUBLICAR_EXIT=4\n{Sq(Path.Combine(arbol.Raiz, "build", "publicar-release.sh"))} {Version}");

        Assert.Equal(7, exitDeploy);
        Assert.Equal(4, exitPublicar);
        Assert.NotEqual(0, h.Ejecutar("test -f /etc/systemd/system/stockapp-api.service").ExitCode);
    }
}
