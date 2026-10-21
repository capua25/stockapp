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

        public Resultado Correr(string[] args, IDictionary<string, string>? env = null)
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
            var (exit, stdout, stderr) = _host.Operador(
                string.Join('\n', exportes) + $"\ncd {Sq(Raiz)}\n./deploy/preparar-vps-nuevo.sh {argumentos}");
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
