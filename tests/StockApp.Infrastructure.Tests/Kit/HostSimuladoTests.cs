using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes del host simulado con estado (<see cref="HostSimulado"/>): el "VPS" contra el que
/// se testea preparar-vps-nuevo.sh. Cada test levanta su propio contenedor ubuntu:24.04
/// (stockapp-test-host-&lt;guid12&gt;) y lo borra por nombre exacto en Dispose.
///
/// Lo que se prueba acá es que los shims se comportan como la herramienta real EN LO QUE
/// IMPORTA a preparar (ufw que deja afuera al operador, certbot que emite aunque su hook falle,
/// systemd-run que falla si el ejecutable no existe, sudo que de verdad eleva): si un shim
/// miente, todos los tests de preparar pasan contra una realidad inventada.
/// </summary>
public class HostSimuladoTests
{
    private const string Dominio = "api.test.invalid";
    private const string Hook = "/usr/local/sbin/stockapp-tls-deploy-hook.sh";

    private static string RaizRepo([CallerFilePath] string archivo = "") => BashLimpio.RaizRepo(archivo);

    /// <summary>ssh falso del operador contra el host; ControlMaster desactivado salvo pedido.</summary>
    private static string Ssh(HostSimulado h, string remoto, bool desactivarMaestro = true) =>
        $"ssh -p 22 -o BatchMode=yes -o ConnectTimeout=5 {(desactivarMaestro ? "-o ControlMaster=no " : "")}" +
        $"{HostSimulado.Usuario}@{h.Host} {Quote(remoto)}";

    /// <summary>Corre el comando en el host y exige exit 0; si no, el mensaje trae la salida.</summary>
    private static void Ok(HostSimulado h, string comando)
    {
        var (exit, salida) = h.Ejecutar(comando);
        Assert.True(exit == 0, $"`{comando}` salió {exit}: {salida}");
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Instala certbot, crea el usuario stockapp y deja el hook REAL del repo en su lugar.</summary>
    private static void PrepararParaCertbot(HostSimulado h)
    {
        Ok(h, "sudo apt-get install -y certbot");
        Assert.Equal(0, h.Ejecutar(
            "sudo useradd --system --create-home --home-dir /var/lib/stockapp --shell /usr/sbin/nologin stockapp").ExitCode);
        h.Escribir(Hook, File.ReadAllText(Path.Combine(RaizRepo(), "deploy", "tls", "stockapp-tls-deploy-hook.sh")));
        Ok(h, $"sudo chmod 0755 {Hook}");
    }

    private static string Emitir =>
        $"sudo certbot certonly --standalone -n --agree-tos --register-unsafely-without-email -d {Dominio} --deploy-hook {Hook}";

    // ---------- Ciclo de vida ----------

    [Fact]
    public void Host_SeBorraPorNombreExacto()
    {
        string nombre;
        using (var h = HostSimulado.Crear())
        {
            nombre = h.Nombre;
            Assert.StartsWith("stockapp-test-host-", nombre);
            Assert.Matches("^stockapp-test-host-[0-9a-f]{12}$", nombre);
            Assert.NotEqual("", EjecutorBash.Ejecutar($"docker ps -a --filter name=^/{nombre}$ -q").Stdout);
        }

        var (exit, stdout, _) = EjecutorBash.Ejecutar($"docker ps -a --filter name=^/{nombre}$ -q");
        Assert.Equal(0, exit);
        Assert.Equal("", stdout);
    }

    // ---------- ufw + ssh: la regla de red del host ----------

    [Fact]
    public void Ufw_EnableSinReglaSsh_RechazaConexionesNuevas()
    {
        using var h = HostSimulado.Crear();
        Assert.Equal(0, h.Operador(Ssh(h, "sudo -n true")).ExitCode);

        Ok(h, "sudo ufw --force enable");

        var (exit, _, stderr) = h.Operador(Ssh(h, "sudo -n true"));
        Assert.Equal(255, exit);
        Assert.Contains("Connection timed out", stderr);
    }

    [Fact]
    public void Ufw_EnableConReglaSsh_Acepta()
    {
        using var h = HostSimulado.Crear();
        Ok(h, "sudo ufw allow 22/tcp");
        Ok(h, "sudo ufw --force enable");

        Assert.Equal(0, h.Operador(Ssh(h, "sudo -n true")).ExitCode);
    }

    [Fact]
    public void Ufw_AllowRepetido_NoDuplicaLaRegla()
    {
        using var h = HostSimulado.Crear();
        var primera = h.Ejecutar("sudo ufw allow 22/tcp");
        var segunda = h.Ejecutar("sudo ufw allow 22/tcp");
        Assert.Equal(0, primera.ExitCode);
        Assert.Contains("Rules updated", primera.Salida);
        Assert.Equal(0, segunda.ExitCode);
        Assert.Contains("Skipping adding existing rule", segunda.Salida);

        h.Ejecutar("sudo ufw --force enable");
        var reglas = h.EstadoUfw().Split('\n').Where(l => l.StartsWith("22/tcp") && !l.Contains("(v6)")).ToList();
        Assert.Single(reglas);
    }

    [Fact]
    public void Ufw_Status_InactivoYActivo()
    {
        using var h = HostSimulado.Crear();
        Assert.Equal("Status: inactive", h.EstadoUfw().Trim());

        h.Ejecutar("sudo ufw allow 80/tcp");
        h.Ejecutar("sudo ufw --force enable");
        var estado = h.EstadoUfw();
        Assert.StartsWith("Status: active", estado);
        Assert.Matches(@"(?m)^80/tcp\s+ALLOW\s+Anywhere\s*$", estado);

        h.Ejecutar("sudo ufw disable");
        Assert.Equal("Status: inactive", h.EstadoUfw().Trim());
    }

    [Fact]
    public void Ufw_Reset_Sale1_YQuedaEnElRastroDelHost()
    {
        using var h = HostSimulado.Crear();
        var (exit, _) = h.Ejecutar("sudo ufw --force reset");
        Assert.Equal(1, exit);
        Assert.Contains("ufw --force reset", h.RastroHost());
    }

    [Fact]
    public void Ufw_ComoNoRoot_Falla()
    {
        using var h = HostSimulado.Crear();
        var (exit, salida) = h.Ejecutar("ufw status");
        Assert.Equal(1, exit);
        Assert.Contains("You need to be root", salida);
    }

    [Fact]
    public void Ufw_VivePorRutaDeLaReversa()
    {
        using var h = HostSimulado.Crear();
        // La reversa del firewall es /usr/sbin/ufw; si el shim no está ahí, systemd-run no la arma.
        Ok(h, "test -x /usr/sbin/ufw");
        Assert.Equal("/usr/sbin/ufw", h.Ejecutar("command -v ufw").Salida.Trim());
    }

    // ---------- ssh falso ----------

    [Fact]
    public void Ssh_ConMaestroActivo_SobreviveAlFirewall()
    {
        using var h = HostSimulado.Crear();
        // ControlMaster sin desactivar: la primera conexión queda de maestro, y las siguientes
        // viajan por ELLA, sin conexión TCP nueva. El firewall no las ve: el re-chequeo "con una
        // conexión nueva" daría OK aunque el enable haya dejado afuera al operador.
        Assert.Equal(0, h.Operador(Ssh(h, "sudo -n true", desactivarMaestro: false)).ExitCode);
        Ok(h, "sudo ufw --force enable");

        Assert.Equal(0, h.Operador(Ssh(h, "sudo -n true", desactivarMaestro: false)).ExitCode);
    }

    [Fact]
    public void Ssh_SinMaestro_NoSobrevive()
    {
        using var h = HostSimulado.Crear();
        Assert.Equal(0, h.Operador(Ssh(h, "sudo -n true")).ExitCode);
        Ok(h, "sudo ufw --force enable");

        Assert.Equal(255, h.Operador(Ssh(h, "sudo -n true")).ExitCode);
    }

    [Fact]
    public void Ssh_ControlPathNone_TambienDesactivaElMaestro()
    {
        using var h = HostSimulado.Crear();
        var cmd = $"ssh -o ControlPath=none -o BatchMode=yes {HostSimulado.Usuario}@{h.Host} true";
        Assert.Equal(0, h.Operador(cmd).ExitCode);
        h.Ejecutar("sudo ufw --force enable");
        Assert.Equal(255, h.Operador(cmd).ExitCode);
    }

    [Fact]
    public void Ssh_RegistraArgsConPercentQ_YNoElStdin()
    {
        using var h = HostSimulado.Crear();
        var (exit, stdout, _) = h.Operador(
            $"printf 'SECRETO-EN-STDIN' | ssh -p 22 -o BatchMode=yes {HostSimulado.Usuario}@{h.Host} 'cat >/dev/null; echo \"hola mundo\"'");
        Assert.Equal(0, exit);
        Assert.Equal("hola mundo", stdout);

        var rastro = h.Rastro();
        Assert.Contains("ARGS: -p 22 -o BatchMode=yes", rastro);
        Assert.Contains("hola\\ mundo", rastro);
        Assert.DoesNotContain("SECRETO-EN-STDIN", rastro);
    }

    [Fact]
    public void Ssh_UsuarioPuertoYHostIncorrectos_Fallan255()
    {
        using var h = HostSimulado.Crear();
        var (exitUsuario, _, errUsuario) = h.Operador($"ssh -o BatchMode=yes root@{h.Host} true");
        Assert.Equal(255, exitUsuario);
        Assert.Contains("Permission denied", errUsuario);

        var (exitPuerto, _, errPuerto) = h.Operador($"ssh -p 2222 -o BatchMode=yes {HostSimulado.Usuario}@{h.Host} true");
        Assert.Equal(255, exitPuerto);
        Assert.Contains("Connection refused", errPuerto);

        var (exitHost, _, errHost) = h.Operador($"ssh -o BatchMode=yes {HostSimulado.Usuario}@otro.test.invalid true");
        Assert.Equal(255, exitHost);
        Assert.Contains("Could not resolve hostname", errHost);
    }

    [Fact]
    public void Ssh_PuertoSshConfigurable()
    {
        using var h = HostSimulado.Crear(puertoSsh: 2222);
        Assert.Equal(0, h.Operador($"ssh -p 2222 -o BatchMode=yes {HostSimulado.Usuario}@{h.Host} true").ExitCode);
        Assert.Equal(255, h.Operador($"ssh -p 22 -o BatchMode=yes {HostSimulado.Usuario}@{h.Host} true").ExitCode);
    }

    [Fact]
    public void Scp_CopiaConElModoDelOrigen_YRespetaElFirewall()
    {
        using var h = HostSimulado.Crear();
        var origen = Path.Combine(h.DirOperador, "compose.yml");
        File.WriteAllText(origen, "name: stockapp\n");
        File.SetUnixFileMode(origen, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var (exit, _, stderr) = h.Operador(
            $"scp -P 22 -o BatchMode=yes -o ControlMaster=no '{origen}' {HostSimulado.Usuario}@{h.Host}:compose-copia.yml");
        Assert.True(exit == 0, stderr);
        Assert.Equal("name: stockapp", h.Ejecutar("cat compose-copia.yml").Salida.Trim());
        Assert.Equal("600", h.Ejecutar("stat -c %a compose-copia.yml").Salida.Trim());
        Assert.Contains("SCP-ARGS:", h.Rastro());

        h.Ejecutar("sudo ufw --force enable");
        var bloqueado = h.Operador(
            $"scp -P 22 -o BatchMode=yes -o ControlMaster=no '{origen}' {HostSimulado.Usuario}@{h.Host}:otro.yml");
        Assert.Equal(255, bloqueado.ExitCode);
    }

    // ---------- certbot ----------

    [Fact]
    public void Certbot_PrimeraEmisionConHookQueFallaEnRestart_DejaLosPemYSaleDistintoDeCero()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);

        var (exit, salida) = h.Ejecutar(Emitir);

        // El hook REAL copia los PEM y después su `systemctl restart stockapp-api` falla (la
        // unit todavía no existe): certbot informa el fallo en el exit aunque emitió.
        Assert.NotEqual(0, exit);
        Assert.Contains("Unit stockapp-api.service not found", salida);
        Assert.Equal("stockapp:stockapp 600", h.Ejecutar("sudo stat -c '%U:%G %a' /var/lib/stockapp/tls/fullchain.pem").Salida.Trim());
        Assert.Equal("stockapp:stockapp 600", h.Ejecutar("sudo stat -c '%U:%G %a' /var/lib/stockapp/tls/privkey.pem").Salida.Trim());
        Ok(h, $"sudo openssl x509 -checkend {30 * 86400} -noout -in /etc/letsencrypt/live/{Dominio}/fullchain.pem");
        Assert.Contains($"renew_hook = {Hook}", h.Leer($"/etc/letsencrypt/renewal/{Dominio}.conf"));
    }

    [Fact]
    public void Certbot_Puerto80Ocupado_Falla()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);
        h.OcuparPuerto80();

        var (exit, salida) = h.Ejecutar(Emitir);

        Assert.NotEqual(0, exit);
        Assert.Contains("Could not bind TCP port 80", salida);
        Assert.NotEqual(0, h.Ejecutar($"sudo test -e /etc/letsencrypt/live/{Dominio}").ExitCode);
    }

    [Fact]
    public void Certbot_ConUfwActivoSinReglaDel80_FallaPorElFirewall()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);
        h.Ejecutar("sudo ufw allow 22/tcp");
        h.Ejecutar("sudo ufw --force enable");

        var (exit, salida) = h.Ejecutar(Emitir);
        Assert.NotEqual(0, exit);
        Assert.Contains("firewall problem", salida);

        h.Ejecutar("sudo ufw allow 80/tcp");
        Assert.Contains("Unit stockapp-api.service not found", h.Ejecutar(Emitir).Salida);
        Ok(h, $"sudo test -e /etc/letsencrypt/live/{Dominio}/fullchain.pem");
    }

    [Fact]
    public void Certbot_CertificadoVigente_NoVuelveAEmitirNiCorreElHook()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);
        h.Ejecutar(Emitir);
        var antes = h.RastroHost().Split('\n').Count(l => l.StartsWith("systemctl"));

        var (exit, salida) = h.Ejecutar(Emitir);

        Assert.Equal(0, exit);
        Assert.Contains("not yet due for renewal", salida);
        Assert.Equal(antes, h.RastroHost().Split('\n').Count(l => l.StartsWith("systemctl")));
    }

    [Fact]
    public void Certbot_RenewForzado_VuelveACorrerElHook()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);
        h.Ejecutar(Emitir);
        var antes = h.RastroHost().Split('\n').Count(l => l.StartsWith("systemctl restart"));

        var (exit, _) = h.Ejecutar($"sudo certbot renew --force-renewal --cert-name {Dominio}");

        Assert.NotEqual(0, exit); // el hook sigue fallando: no hay unit
        Assert.Equal(antes + 1, h.RastroHost().Split('\n').Count(l => l.StartsWith("systemctl restart")));
    }

    [Fact]
    public void Certbot_ConHookQueAndaYUnitInstalada_SaleCero()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);
        h.Escribir("/etc/systemd/system/stockapp-api.service", "[Service]\nExecStart=/bin/true\n");

        var (exit, salida) = h.Ejecutar(Emitir);

        Assert.True(exit == 0, salida);
        Assert.Equal("active", h.Ejecutar("systemctl is-active stockapp-api").Salida.Trim());
    }

    [Fact]
    public void Certbot_PrimeraEmisionSinCorreoNiRegisterUnsafely_SeNiegaYNoEscribeNada()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);

        var (exit, salida) = h.Ejecutar(
            $"sudo certbot certonly --standalone -n --agree-tos -d {Dominio} --deploy-hook {Hook}");

        // Cuenta ACME nueva + -n: el real no puede preguntar el correo.
        Assert.Equal(1, exit);
        Assert.Contains("Missing command line flag", salida);
        Assert.NotEqual(0, h.Ejecutar($"sudo test -e /etc/letsencrypt/live/{Dominio}").ExitCode);
        Assert.NotEqual(0, h.Ejecutar($"sudo test -e /etc/letsencrypt/renewal/{Dominio}.conf").ExitCode);
        Assert.DoesNotContain("systemctl", h.RastroHost());
    }

    [Fact]
    public void Certbot_PrimeraEmisionSinAgreeTos_SeNiega()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);

        var (exit, salida) = h.Ejecutar(
            $"sudo certbot certonly --standalone -n --register-unsafely-without-email -d {Dominio}");

        Assert.Equal(1, exit);
        Assert.Contains("Missing command line flag", salida);
    }

    [Fact]
    public void Certbot_ConCorreoOConNoEffEmail_Emite_YConCuentaYaRegistradaNoPideCorreo()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);

        var (exit, salida) = h.Ejecutar(
            $"sudo certbot certonly --standalone -n --agree-tos -m ops@example.invalid --no-eff-email -d {Dominio} --deploy-hook {Hook}");
        Assert.Contains("Successfully received certificate", salida);
        Assert.NotEqual(0, exit); // el hook falla: no hay unit
        Ok(h, $"sudo test -e /etc/letsencrypt/live/{Dominio}/fullchain.pem");

        // La cuenta quedó registrada: una emisión forzada de otro dominio ya no pide correo.
        var (_, segunda) = h.Ejecutar(
            "sudo certbot certonly --standalone -n --agree-tos -d otro.test.invalid --deploy-hook " + Hook);
        Assert.Contains("Successfully received certificate", segunda);
    }

    [Fact]
    public void Certbot_Reconfigure_ReescribeElRenewHookSinCorrerloYFallaSiNoHayLineage()
    {
        using var h = HostSimulado.Crear();
        PrepararParaCertbot(h);
        h.Ejecutar(Emitir);
        var antes = h.RastroHost().Split('\n').Count(l => l.StartsWith("systemctl restart"));

        var (exit, salida) = h.Ejecutar(
            $"sudo certbot reconfigure --cert-name {Dominio} --deploy-hook /usr/local/sbin/otro-hook.sh");

        Assert.True(exit == 0, salida);
        var conf = h.Leer($"/etc/letsencrypt/renewal/{Dominio}.conf");
        Assert.Contains("renew_hook = /usr/local/sbin/otro-hook.sh", conf);
        Assert.DoesNotContain($"renew_hook = {Hook}", conf);
        Assert.Equal(antes, h.RastroHost().Split('\n').Count(l => l.StartsWith("systemctl restart")));

        var (exitNo, salidaNo) = h.Ejecutar("sudo certbot reconfigure --cert-name no.existe.invalid --deploy-hook /x");
        Assert.Equal(1, exitNo);
        Assert.Contains("no renewal configuration file", salidaNo);
    }

    [Fact]
    public void Certbot_AntesDeInstalar_NoExiste_YComoNoRootPideRoot()
    {
        using var h = HostSimulado.Crear();
        var sinInstalar = h.Ejecutar("sudo certbot --version");
        Assert.Equal(1, sinInstalar.ExitCode); // sudo: certbot: command not found
        Assert.Contains("command not found", sinInstalar.Salida);

        h.Ejecutar("sudo apt-get install -y certbot");
        var comoUsuario = h.Ejecutar($"certbot certonly --standalone -n --agree-tos --register-unsafely-without-email -d {Dominio}");
        Assert.Equal(1, comoUsuario.ExitCode);
        Assert.Contains("Either run as root", comoUsuario.Salida);
    }

    // ---------- systemctl y systemd-run ----------

    [Fact]
    public void Systemctl_StartSinArchivoDeUnit_Falla5()
    {
        using var h = HostSimulado.Crear();
        var (exit, salida) = h.Ejecutar("sudo systemctl start stockapp-api");
        Assert.Equal(5, exit);
        Assert.Contains("Unit stockapp-api.service not found.", salida);
    }

    [Fact]
    public void Systemctl_ComoNoRoot_PideAutenticacion()
    {
        using var h = HostSimulado.Crear();
        h.Escribir("/etc/systemd/system/stockapp-api.service", "[Service]\nExecStart=/bin/true\n");
        var (exit, salida) = h.Ejecutar("systemctl start stockapp-api");
        Assert.Equal(1, exit);
        Assert.Contains("Interactive authentication required", salida);
        // Las lecturas no piden nada.
        Assert.Equal(3, h.Ejecutar("systemctl is-active stockapp-api").ExitCode);
    }

    [Fact]
    public void Systemctl_UnitInstalada_StartEnableYEstados()
    {
        using var h = HostSimulado.Crear();
        h.Escribir("/etc/systemd/system/stockapp-api.service", "[Service]\nExecStart=/bin/true\n");

        Assert.Equal(("inactive", 3), Estado(h, "is-active"));
        Assert.Equal(("disabled", 1), Estado(h, "is-enabled"));
        Ok(h, "sudo systemctl enable stockapp-api");
        Ok(h, "sudo systemctl start stockapp-api");
        Assert.Equal(("active", 0), Estado(h, "is-active"));
        Assert.Equal(("enabled", 0), Estado(h, "is-enabled"));
        Ok(h, "sudo systemctl stop stockapp-api");
        Assert.Equal(("inactive", 3), Estado(h, "is-active"));
    }

    private static (string, int) Estado(HostSimulado h, string verbo)
    {
        var (exit, salida) = h.Ejecutar($"systemctl {verbo} stockapp-api 2>&1");
        return (salida.Trim(), exit);
    }

    [Fact]
    public void SystemdRun_ArmaElTimer_YStopLoDesarma()
    {
        using var h = HostSimulado.Crear();
        var (exitRun, salidaRun) = h.Ejecutar("sudo systemd-run --unit=stockapp-ufw-reversa --on-active=120 /usr/sbin/ufw disable");
        Assert.Equal(0, exitRun);
        Assert.Contains("Running timer as unit: stockapp-ufw-reversa.timer", salidaRun);

        Assert.Contains("OnActiveSec=120", h.Leer("/run/systemd/transient/stockapp-ufw-reversa.timer"));
        Assert.Contains("stockapp-ufw-reversa.timer", h.Ejecutar("systemctl list-timers --all").Salida);
        Assert.Equal("active", h.Ejecutar("systemctl is-active stockapp-ufw-reversa.timer").Salida.Trim());

        Ok(h, "sudo systemctl stop stockapp-ufw-reversa.timer");

        Assert.NotEqual(0, h.Ejecutar("test -e /run/systemd/transient/stockapp-ufw-reversa.timer").ExitCode);
        Assert.DoesNotContain("stockapp-ufw-reversa", h.Ejecutar("systemctl list-timers --all").Salida);
        var (exitInactivo, salidaInactivo) = h.Ejecutar("systemctl is-active stockapp-ufw-reversa.timer");
        Assert.Equal((3, "inactive"), (exitInactivo, salidaInactivo.Trim()));
        // Parar un timer que ya no existe falla con 5, como el systemctl real.
        var (exitOtraVez, salidaOtraVez) = h.Ejecutar("sudo systemctl stop stockapp-ufw-reversa.timer");
        Assert.Equal(5, exitOtraVez);
        Assert.Contains("not loaded", salidaOtraVez);
    }

    [Fact]
    public void SystemdRun_EjecutableInexistente_Falla()
    {
        using var h = HostSimulado.Crear();
        var (exit, salida) = h.Ejecutar("sudo systemd-run --unit=stockapp-ufw-reversa --on-active=120 /usr/sbin/no-existe disable");
        Assert.Equal(1, exit);
        Assert.Contains("Failed to find executable /usr/sbin/no-existe", salida);
        Assert.NotEqual(0, h.Ejecutar("test -e /run/systemd/transient/stockapp-ufw-reversa.timer").ExitCode);
    }

    [Fact]
    public void SystemdRun_UnidadYaArmada_Falla()
    {
        using var h = HostSimulado.Crear();
        const string arma = "sudo systemd-run --unit=stockapp-ufw-reversa --on-active=120 /usr/sbin/ufw disable";
        Ok(h, arma);
        var (exit, salida) = h.Ejecutar(arma);
        Assert.Equal(1, exit);
        Assert.Contains("already loaded or has a fragment file", salida);
    }

    [Fact]
    public void SystemdRun_LaReversaDisparada_DeshaceElFirewall()
    {
        using var h = HostSimulado.Crear();
        h.Ejecutar("sudo ufw --force enable");
        h.Ejecutar("sudo systemd-run --unit=stockapp-ufw-reversa --on-active=120 /usr/sbin/ufw disable");

        // Disparar el timer == arrancar el servicio que activa: corre el comando armado.
        Ok(h, "sudo systemctl start stockapp-ufw-reversa.service");

        Assert.Equal("Status: inactive", h.EstadoUfw().Trim());
        Assert.NotEqual(0, h.Ejecutar("test -e /run/systemd/transient/stockapp-ufw-reversa.timer").ExitCode);
    }

    // ---------- sudo ----------

    [Fact]
    public void Sudo_EleVaARootDeVerdad()
    {
        using var h = HostSimulado.Crear();
        Assert.Equal("1001", h.Ejecutar("id -u").Salida.Trim());
        Assert.Equal("0", h.Ejecutar("sudo id -u").Salida.Trim());
        // Y lo que hace como root es real: crea un archivo que el usuario no podría crear.
        Ok(h, "sudo install -d -o root -g root -m 700 /root/prueba");
        Assert.NotEqual(0, h.Ejecutar("install -d -m 700 /root/otra").ExitCode);
    }

    [Fact]
    public void Sudo_SinSudoN_PideContrasena()
    {
        using var h = HostSimulado.Crear();
        h.Escribir("/var/lib/hostsim/sudo-pide-contrasena", "");

        var (exit, salida) = h.Ejecutar("sudo -n true");
        Assert.Equal(1, exit);
        Assert.Contains("sudo: a password is required", salida);
    }

    // ---------- herramientas ausentes en ubuntu:24.04 pelado ----------

    [Fact]
    public void Openssl_EsElRealDelOperador()
    {
        using var h = HostSimulado.Crear();
        Assert.StartsWith("OpenSSL 3", h.Ejecutar("openssl version").Salida);
        Assert.Equal(32, h.Ejecutar("openssl rand -base64 24").Salida.Trim().Length);
    }

    [Fact]
    public void Ss_ListaElPuertoSsh_YElOcupado()
    {
        using var h = HostSimulado.Crear();
        var antes = h.Ejecutar("ss -ltn").Salida;
        Assert.Contains("0.0.0.0:22 ", antes + " ");
        Assert.DoesNotContain(":80 ", antes + " ");

        h.OcuparPuerto80();
        Assert.Contains(":80 ", h.Ejecutar("ss -ltn").Salida + " ");
        Assert.Contains(":80 ", h.Ejecutar("ss -ltn 'sport = :80'").Salida + " ");
        Assert.DoesNotContain(":22 ", h.Ejecutar("ss -ltn 'sport = :80'").Salida + " ");
    }

    [Fact]
    public void Journalctl_ReflejaLosArranquesDelSystemctl()
    {
        using var h = HostSimulado.Crear();
        h.Escribir("/etc/systemd/system/stockapp-api.service", "[Service]\nExecStart=/bin/true\n");
        Assert.Contains("-- No entries --", h.Ejecutar("journalctl -u stockapp-api --no-pager").Salida);

        h.Ejecutar("sudo systemctl start stockapp-api");
        var (exit, salida) = h.Ejecutar("journalctl -u stockapp-api --no-pager");
        Assert.Equal(0, exit);
        Assert.Contains("Started stockapp-api.service", salida);
        Assert.Contains("Started stockapp-api.service", h.Ejecutar("journalctl -u stockapp-api --no-pager --since '1 minute ago'").Salida);
        Assert.Contains("-- No entries --", h.Ejecutar("journalctl -u stockapp-api --no-pager --since '+1 hour'").Salida);
    }

    // ---------- apt-get y docker ----------

    [Fact]
    public void AptGet_DockerIo_CreaElGrupo_YUsermodSoloVaEnSesionNueva()
    {
        using var h = HostSimulado.Crear();
        Assert.Equal(127, h.Ejecutar("docker ps").ExitCode); // antes de instalar no existe

        Ok(h, "sudo apt-get install -y docker.io docker-compose-v2");
        Ok(h, "getent group docker");

        var sinGrupo = h.Ejecutar("docker ps");
        Assert.Equal(1, sinGrupo.ExitCode);
        Assert.Contains("permission denied while trying to connect to the Docker daemon socket", sinGrupo.Salida);

        Ok(h, $"sudo usermod -aG docker {HostSimulado.Usuario}");
        Ok(h, "docker ps"); // cada Ejecutar es una sesión nueva
        Assert.Contains("Docker Compose version v2", h.Ejecutar("docker compose version").Salida);
    }

    [Fact]
    public void AptGet_PaqueteInexistente_Falla100_YSinRootTambien()
    {
        using var h = HostSimulado.Crear();
        var (exit, salida) = h.Ejecutar("sudo apt-get install -y paquete-que-no-existe");
        Assert.Equal(100, exit);
        Assert.Contains("E: Unable to locate package paquete-que-no-existe", salida);

        var (exitUsuario, salidaUsuario) = h.Ejecutar("apt-get install -y certbot");
        Assert.Equal(100, exitUsuario);
        Assert.Contains("are you root?", salidaUsuario);
    }

    private static void DockerListo(HostSimulado h)
    {
        Ok(h, "sudo apt-get install -y docker.io docker-compose-v2");
        Ok(h, $"sudo usermod -aG docker {HostSimulado.Usuario}");
    }

    [Fact]
    public void Docker_ComposeUp_CreaStockappPgHealthy_ExigiendoLasVariablesDelCompose()
    {
        using var h = HostSimulado.Crear();
        DockerListo(h);
        h.Escribir("/home/" + HostSimulado.Usuario + "/stockapp-deploy/docker-compose.postgres.yml",
            File.ReadAllText(Path.Combine(RaizRepo(), "deploy", "docker-compose.postgres.yml")));
        h.Escribir("/home/" + HostSimulado.Usuario + "/stockapp-deploy/.env", "POSTGRES_USER=stockapp\nPOSTGRES_DB=stockapp\n");
        const string up = "cd stockapp-deploy && docker compose --env-file .env -f docker-compose.postgres.yml up -d";

        var sinPassword = h.Ejecutar(up);
        Assert.Equal(1, sinPassword.ExitCode);
        Assert.Contains("required variable POSTGRES_PASSWORD is missing a value", sinPassword.Salida);
        Assert.Equal("", h.Ejecutar("docker ps -a --format '{{.Names}}'").Salida.Trim());

        h.Escribir("/home/" + HostSimulado.Usuario + "/stockapp-deploy/.env",
            "POSTGRES_USER=stockapp\nPOSTGRES_DB=stockapp\nPOSTGRES_PASSWORD=x\n");
        Ok(h, up);
        Assert.Equal("stockapp-pg", h.Ejecutar("docker ps -a --format '{{.Names}}'").Salida.Trim());
        Assert.Equal("healthy", h.Ejecutar("docker inspect -f '{{.State.Health.Status}}' stockapp-pg").Salida.Trim());
        Assert.Contains("127.0.0.1:5433", h.Ejecutar("ss -ltn").Salida);
        Assert.Contains("Running", h.Ejecutar(up).Salida); // idempotente
    }

    [Fact]
    public void Docker_ContenedorAjeno_AparecePeroNoEsStockapp()
    {
        using var h = HostSimulado.Crear();
        DockerListo(h);
        h.PlantarContenedorAjeno("pinar-nginx-1");

        Assert.Equal("pinar-nginx-1", h.Ejecutar("docker ps -a --format '{{.Names}}'").Salida.Trim());
        Assert.Equal("pinar-nginx-1", h.Ejecutar("docker ps --format '{{.Names}}'").Salida.Trim());
    }

    [Fact]
    public void Docker_PsqlResponde_SoloLoQueElTestPlanto()
    {
        using var h = HostSimulado.Crear();
        DockerListo(h);
        h.PlantarContenedorAjeno("stockapp-pg");
        h.PlantarRespuestaPsql("information_schema.tables", "0");
        h.PlantarRespuestaPsql("LogsAuditoria", "boom", exitCode: 3);

        var vacia = h.Ejecutar("docker exec -i stockapp-pg psql -U stockapp -d stockapp -tA <<< \"SELECT count(*) FROM information_schema.tables\"");
        Assert.Equal(0, vacia.ExitCode);
        Assert.Equal("0", vacia.Salida.Trim());

        var falla = h.Ejecutar("docker exec -i stockapp-pg psql -U stockapp -d stockapp -tA -c 'SELECT count(*) FROM \"LogsAuditoria\"'");
        Assert.Equal(3, falla.ExitCode);

        var sinRespuesta = h.Ejecutar("docker exec -i stockapp-pg psql -U stockapp -d stockapp -tA <<< 'SELECT 1'");
        Assert.Equal(1, sinRespuesta.ExitCode);
        Assert.Contains("sin respuesta planteada", sinRespuesta.Salida);
    }

    // ---------- operador: getent y curl ----------

    [Fact]
    public void Getent_ResuelveSoloLoRegistrado()
    {
        using var h = HostSimulado.Crear();
        Assert.Equal(0, h.Operador($"getent ahostsv4 {h.Host}").ExitCode);
        Assert.Contains(HostSimulado.IpDelHost, h.Operador($"getent ahostsv4 {h.Host}").Stdout);

        Assert.Equal(2, h.Operador($"getent ahostsv4 {Dominio}").ExitCode);
        h.ResolverDominio(Dominio);
        Assert.Contains(HostSimulado.IpDelHost, h.Operador($"getent ahostsv4 {Dominio}").Stdout);
        h.ResolverDominio("otro.test.invalid", "198.51.100.7");
        Assert.StartsWith("198.51.100.7", h.Operador("getent ahostsv4 otro.test.invalid").Stdout);
    }

    [Fact]
    public void Curl_SirveLoPlantado_RespetaFirewallYRegistraArgs()
    {
        using var h = HostSimulado.Crear();
        h.ResolverDominio(Dominio);
        h.EscucharEn(8443);
        h.PlantarRespuestaHttp(8443, "/licencia/estado", "{\"activada\":true}");
        var url = $"https://{Dominio}:8443/licencia/estado";

        var ok = h.Operador($"curl -fsS --max-time 5 {url}");
        Assert.Equal(0, ok.ExitCode);
        Assert.Equal("{\"activada\":true}", ok.Stdout);
        Assert.Contains("CURL-ARGS: -fsS --max-time 5 " + url, h.Rastro());

        var noExiste = h.Operador($"curl -fsS https://{Dominio}:8443/otra");
        Assert.Equal(22, noExiste.ExitCode);
        Assert.Contains("returned error: 404", noExiste.Stderr);

        var sinResolver = h.Operador("curl -fsS https://nadie.test.invalid:8443/");
        Assert.Equal(6, sinResolver.ExitCode);

        var cerrado = h.Operador($"curl -fsS https://{Dominio}:9999/");
        Assert.Equal(7, cerrado.ExitCode);

        h.Ejecutar("sudo ufw --force enable");
        Assert.Equal(28, h.Operador($"curl -fsS --max-time 5 {url}").ExitCode);
    }
}
