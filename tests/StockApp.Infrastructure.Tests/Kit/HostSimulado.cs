using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using StockApp.Tests.Compartido;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// "VPS" de mentira con estado para testear preparar-vps-nuevo.sh (spec 8.1): un contenedor
/// ubuntu:24.04 (stockapp-test-host-&lt;guid12&gt;, sin red) con shims de ufw, certbot, apt-get,
/// systemctl, systemd-run, docker, sudo, ss y journalctl que guardan su estado en archivos y se
/// comportan como la herramienta real en lo que importa (cada shim cita su versión y lo que NO
/// simula en el encabezado; ver Kit/HostSimulado/).
///
/// Del lado del operador, <see cref="DirOperador"/> trae ssh, scp, curl y getent falsos (al
/// frente del PATH con <see cref="Operador"/>, o a mano) más el rastro de lo que se invocó. El
/// ssh falso aplica la regla de red del host: con ufw activo y sin regla para el puerto, una
/// conexión NUEVA sale con 255; una conexión por un ControlMaster ya establecido, no (peligro
/// real que el test del firewall tiene que poder detectar).
///
/// Las sesiones (<see cref="Ejecutar"/>, ssh) corren como el usuario de deploy (no root); el
/// `sudo` del host eleva de verdad con una copia setuid de setpriv. El contenedor se borra por
/// nombre exacto en <see cref="Dispose"/>; nunca se toca otro contenedor.
/// </summary>
internal sealed class HostSimulado : IDisposable
{
    public const string Usuario = "stockapp-deploy-user";
    public const string IpDelHost = "192.0.2.10";
    private const string Estado = "/var/lib/hostsim";
    private const string PathDeSesion =
        "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:/usr/games:/usr/local/games:/snap/bin";

    private int _siguientePsql = 1;
    private bool _liberado;

    private HostSimulado(string host, int puertoSsh)
    {
        Host = host;
        PuertoSsh = puertoSsh;
        Nombre = "stockapp-test-host-" + Guid.NewGuid().ToString("N")[..12];
        DirOperador = Path.Combine(Path.GetTempPath(), "stockapp-hostsim-" + Guid.NewGuid().ToString("N")[..12]);
    }

    public string Host { get; }
    public int PuertoSsh { get; }
    public string Nombre { get; }
    public string DirOperador { get; }

    private static string Fuentes([CallerFilePath] string archivo = "") =>
        Path.Combine(Path.GetDirectoryName(archivo)!, "HostSimulado");

    // El mismo certificado autofirmado para todos los hosts de la corrida: generarlo cuesta un
    // RSA de 2048 bits y el certbot falso solo necesita que exista y que venza en 90 días.
    private static readonly Lazy<string> DirCertificado = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "stockapp-hostsim-cert-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        };
        var (exit, _, stderr) = EjecutorBash.Ejecutar(
            $"openssl req -x509 -days 90 -newkey rsa:2048 -nodes -subj /CN=hostsim.test.invalid " +
            $"-keyout '{dir}/key.pem' -out '{dir}/cert.pem'");
        if (exit != 0) throw new InvalidOperationException($"openssl req falló: {stderr}");
        return dir;
    });

    public static HostSimulado Crear(string host = "vps-nuevo.test.invalid", int puertoSsh = 22)
    {
        var h = new HostSimulado(host, puertoSsh);
        try
        {
            h.Levantar();
            return h;
        }
        catch
        {
            h.Dispose();
            throw;
        }
    }

    private void Levantar()
    {
        Directory.CreateDirectory(DirOperador);

        Docker($"run -d --name {Nombre} --network none ubuntu:24.04 sleep infinity");
        Docker($"cp \"{Fuentes()}/.\" {Nombre}:/opt/hostsim-src");
        // El openssl REAL del operador (mismo build de 24.04; las libs ya están en la imagen).
        Docker($"cp /usr/bin/openssl {Nombre}:/usr/local/bin/openssl");
        Docker($"cp \"{DirCertificado.Value}/.\" {Nombre}:/opt/hostsim-cert");
        Docker($"exec {Nombre} bash /opt/hostsim-src/instalar-en-el-host.sh {PuertoSsh}");

        foreach (var archivo in Directory.GetFiles(Path.Combine(Fuentes(), "operador")))
            File.Copy(archivo, Path.Combine(DirOperador, Path.GetFileName(archivo)), overwrite: true);
        File.WriteAllText(Path.Combine(DirOperador, "hostsim.env"),
            $"HS_CONTENEDOR='{Nombre}'\nHS_HOST='{Host}'\nHS_IP='{IpDelHost}'\nHS_PUERTO_SSH='{PuertoSsh}'\n" +
            $"HS_USUARIO='{Usuario}'\nHS_RASTRO='{Path.Combine(DirOperador, "rastro.txt")}'\n");
        File.WriteAllText(Path.Combine(DirOperador, "dns"), $"{Host} {IpDelHost}\n");
        File.WriteAllText(Path.Combine(DirOperador, "rastro.txt"), "");
    }

    private static string Docker(string argumentos)
    {
        var (exit, stdout, stderr) = EjecutorBash.Ejecutar($"docker {argumentos}");
        if (exit != 0)
            throw new InvalidOperationException($"docker {argumentos} falló ({exit}): {stderr}\n{stdout}");
        return stdout;
    }

    private static string Sq(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Rastro ordenado del lado del operador: ARGS: del ssh, SCP-ARGS: y CURL-ARGS:.</summary>
    public string Rastro() => File.ReadAllText(Path.Combine(DirOperador, "rastro.txt"));

    /// <summary>Una línea por invocación de un shim DENTRO del host (por ejemplo `ufw reset`).</summary>
    public string RastroHost() => Leer($"{Estado}/rastro-host");

    /// <summary>Lee un archivo del host como root; lanza si no existe.</summary>
    public string Leer(string rutaEnElHost)
    {
        var (exit, stdout, stderr) = EjecutorBash.Ejecutar(
            $"docker exec -u 0 {Nombre} cat -- {Sq(rutaEnElHost)}");
        if (exit != 0) throw new InvalidOperationException($"No se pudo leer {rutaEnElHost}: {stderr}");
        return stdout;
    }

    /// <summary>
    /// Escribe un archivo en el host como root (crea los directorios que falten). Lo creado
    /// queda con el dueño del primer ancestro existente: bajo el HOME del usuario de deploy,
    /// del usuario de deploy.
    /// </summary>
    public void Escribir(string ruta, string contenido)
    {
        var temporal = Path.Combine(DirOperador, "escribir-" + Guid.NewGuid().ToString("N")[..8]);
        File.WriteAllText(temporal, contenido);
        try
        {
            const string script =
                "d=\"$(dirname -- \"$1\")\"; a=\"$d\"; while [[ ! -e \"$a\" ]]; do a=\"$(dirname -- \"$a\")\"; done; " +
                "o=\"$(stat -c %u:%g -- \"$a\")\"; mkdir -p -- \"$d\"; cat > \"$1\"; " +
                "p=\"$d\"; while [[ \"$p\" != \"$a\" ]]; do chown \"$o\" \"$p\"; p=\"$(dirname -- \"$p\")\"; done; " +
                "chown \"$o\" \"$1\"";
            var (exit, _, stderr) = EjecutorBash.Ejecutar(
                $"docker exec -i -u 0 {Nombre} bash -c {Sq(script)} _ {Sq(ruta)} < {Sq(temporal)}");
            if (exit != 0) throw new InvalidOperationException($"No se pudo escribir {ruta}: {stderr}");
        }
        finally
        {
            File.Delete(temporal);
        }
    }

    /// <summary>
    /// Corre un comando en una sesión nueva del host como el usuario de deploy (bash -c, el
    /// PATH de las sesiones ssh de Ubuntu, sin red de por medio). La salida es stdout y stderr
    /// juntos.
    /// </summary>
    public (int ExitCode, string Salida) Ejecutar(string comando) => EjecutarComo(Usuario, comando);

    private (int ExitCode, string Salida) EjecutarComo(string usuario, string comando)
    {
        var home = usuario == "root" ? "/root" : $"/home/{usuario}";
        var (exit, stdout, _) = EjecutorBash.Ejecutar(
            $"docker exec -i -u {usuario} -w {home} -e HOME={home} -e USER={usuario} -e LOGNAME={usuario} " +
            $"-e PATH={PathDeSesion} {Nombre} bash -c {Sq(comando)} 2>&1 < /dev/null");
        return (exit, stdout);
    }

    /// <summary>
    /// Corre un script de bash en la máquina del operador con <see cref="DirOperador"/> al frente
    /// del PATH (ssh, scp, curl y getent falsos), con la higiene de entorno de BashLimpio.
    /// </summary>
    public (int ExitCode, string Stdout, string Stderr) Operador(string script) =>
        BashLimpio.Ejecutar($"export PATH={Sq(DirOperador)}:\"$PATH\"\n{script}");

    /// <summary>Planta un "contenedor" de otra app (para el guardián de contenedores ajenos).</summary>
    public void PlantarContenedorAjeno(string nombre)
    {
        if (!Regex.IsMatch(nombre, "^[a-z0-9][a-z0-9_.-]*$"))
            throw new ArgumentException($"Nombre de contenedor inválido: {nombre}", nameof(nombre));
        var (exit, salida) = EjecutarComo("root",
            $"install -d -m 1777 {Estado}/docker/{nombre} && echo running > {Estado}/docker/{nombre}/estado");
        if (exit != 0) throw new InvalidOperationException($"No se pudo plantar {nombre}: {salida}");
    }

    /// <summary>Hace que algo escuche en el puerto 80 (certbot --standalone no puede bindearlo).</summary>
    public void OcuparPuerto80() => EscucharEn(80);

    /// <summary>Hace que algo escuche en todas las interfaces del puerto (lo ven `ss` y la red del host).</summary>
    public void EscucharEn(int puerto)
    {
        var (exit, salida) = EjecutarComo("root",
            $"grep -qxF {puerto} {Estado}/puertos || echo {puerto} >> {Estado}/puertos");
        if (exit != 0) throw new InvalidOperationException($"No se pudo abrir el puerto {puerto}: {salida}");
    }

    /// <summary>Hace que `getent` del operador resuelva el dominio (por defecto, a la IP del host).</summary>
    public void ResolverDominio(string dominio, string? ip = null)
    {
        var archivo = Path.Combine(DirOperador, "dns");
        var lineas = File.ReadAllLines(archivo).Where(l => !l.StartsWith(dominio + " ")).ToList();
        lineas.Add($"{dominio} {ip ?? IpDelHost}");
        File.WriteAllLines(archivo, lineas);
    }

    /// <summary>Planta la respuesta que el curl del operador obtiene de http(s)://host:puerto/ruta.</summary>
    public void PlantarRespuestaHttp(int puerto, string ruta, string cuerpo)
    {
        var clave = ruta == "/" ? "_raiz" : ruta.TrimStart('/');
        Escribir($"{Estado}/http/{puerto}/{clave}", cuerpo);
    }

    /// <summary>
    /// Planta lo que responde `docker exec stockapp-pg psql` cuando la consulta CONTIENE el patrón
    /// (texto fijo). Gana el primero que matchea, en el orden en que se plantaron.
    /// </summary>
    public void PlantarRespuestaPsql(string patron, string respuesta, int exitCode = 0)
    {
        var id = (_siguientePsql++).ToString("D2");
        Escribir($"{Estado}/psql/{id}.patron", patron);
        Escribir($"{Estado}/psql/{id}.respuesta", respuesta + "\n");
        Escribir($"{Estado}/psql/{id}.exit", exitCode.ToString());
    }

    /// <summary>Salida de `ufw status` (como root).</summary>
    public string EstadoUfw() => EjecutarComo("root", "ufw status").Salida;

    public void Dispose()
    {
        if (_liberado) return;
        _liberado = true;
        // Por nombre exacto: nunca un patrón, nunca otro contenedor.
        EjecutorBash.Ejecutar($"docker rm -f -v {Nombre}");
        if (Directory.Exists(DirOperador)) Directory.Delete(DirOperador, recursive: true);
    }
}
