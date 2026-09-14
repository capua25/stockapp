using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/kit/00-preflight.sh y de resolver_api_port en
/// deploy/kit/lib/validaciones.sh.
///
/// resolver_api_port implementa la Decisión 1 (RESUELTA: el puerto de la API es configurable):
/// se lee de /etc/stockapp/.env con fallback silencioso a 5080 si el archivo no existe. Que no
/// exista es el camino feliz de una instalación virgen -- 01-bootstrap.sh (Task 3.4) es quien lo
/// CREA, así que 00-preflight.sh corre siempre antes de que exista.
///
/// Los tests de "mapa de severidades" (Decisión 5, Fase 2 / Task 2.2) verifican que el puerto
/// de la API ocupado es AVISO (no bloqueante: el operador puede elegir otro antes de correr
/// 01-bootstrap.sh) mientras que el puerto de Postgres ocupado por algo que no sea el
/// contenedor stockapp-pg es ROJO (Decisión 4: el puerto 5433 es fijo, sin escape para el
/// operador).
///
/// ENV_KIT y PG_PORT son overrides por variable de entorno, nunca expuestos como flag de CLI ni
/// documentados para el operador: en producción ambos caen a su valor por defecto
/// (/etc/stockapp/.env y 5433). El de PG_PORT ya viene así en el diseño (para que una segunda
/// corrida no vea "ocupado" el puerto que levantó el propio 01-bootstrap.sh); ENV_KIT se agrega
/// acá con el mismo patrón que log.sh usa para KIT_LOG. No violan la Decisión 4: ese "sin
/// escape" es sobre no darle al operador una forma de configurar el puerto de Postgres, no
/// sobre que un test no pueda inyectar un valor de prueba.
/// </summary>
public class PreflightTests
{
    private static string RutaLib([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..",
            "deploy", "kit", "lib", "validaciones.sh"));
    }

    private static string RutaScript([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..",
            "deploy", "kit", "00-preflight.sh"));
    }

    /// <summary>Puerto TCP libre real, para no pisar nada que ya esté corriendo en la máquina.</summary>
    private static int PuertoLibre()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var puerto = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return puerto;
    }

    // ---------- resolver_api_port (Decisión 1) ----------

    private static string ResolverApiPort(string archivoEnv, string? apiPortEnv = null)
    {
        var lib = RutaLib();
        Assert.True(File.Exists(lib), $"No se encontró la librería bash en: {lib}");
        var prefijo = apiPortEnv is null ? "" : $"API_PORT='{apiPortEnv}'; ";
        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(
            $"{prefijo}source '{lib}'; resolver_api_port '{archivoEnv}'");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
        return stdout;
    }

    [Fact]
    public void ResolverApiPort_SinArchivo_UsaDefault5080()
        => Assert.Equal("5080", ResolverApiPort("/no/existe/stockapp.env"));

    [Fact]
    public void ResolverApiPort_ArchivoSinLineaApiPort_UsaDefault5080()
    {
        var archivo = Path.GetTempFileName();
        try
        {
            File.WriteAllText(archivo, "POSTGRES_PASSWORD=algo\n");
            Assert.Equal("5080", ResolverApiPort(archivo));
        }
        finally
        {
            File.Delete(archivo);
        }
    }

    [Fact]
    public void ResolverApiPort_ArchivoConApiPort_UsaElValorDelArchivo()
    {
        var archivo = Path.GetTempFileName();
        try
        {
            File.WriteAllText(archivo, "API_PORT=8080\n");
            Assert.Equal("8080", ResolverApiPort(archivo));
        }
        finally
        {
            File.Delete(archivo);
        }
    }

    [Fact]
    public void ResolverApiPort_ArchivoConApiPortRepetido_UsaLaUltimaLinea()
    {
        var archivo = Path.GetTempFileName();
        try
        {
            File.WriteAllText(archivo, "API_PORT=8080\nAPI_PORT=9090\n");
            Assert.Equal("9090", ResolverApiPort(archivo));
        }
        finally
        {
            File.Delete(archivo);
        }
    }

    [Fact]
    public void ResolverApiPort_SinArchivoConVariableDeEntornoPreseteada_UsaLaVariable()
        => Assert.Equal("9999", ResolverApiPort("/no/existe/stockapp.env", apiPortEnv: "9999"));

    // ---------- mapa de severidades: 00-preflight.sh de punta a punta ----------

    private static (int ExitCode, string Stdout) EjecutarPreflight(
        int apiPort, int pgPort, string envKit = "/no/existe/stockapp.env",
        string? composePostgres = null)
    {
        var script = RutaScript();
        Assert.True(File.Exists(script), $"No se encontró el script en: {script}");
        var compose = composePostgres is null ? "" : $"COMPOSE_POSTGRES='{composePostgres}' ";
        var (exitCode, stdout, _) = EjecutorBash.Ejecutar(
            $"ENV_KIT='{envKit}' API_PORT='{apiPort}' PG_PORT='{pgPort}' {compose}bash '{script}'");
        return (exitCode, stdout);
    }

    /// <summary>Versión major de pg_dump instalado en esta máquina (el guardián 2 la necesita
    /// para armar un docker-compose de prueba cuyo tag coincida o difiera a propósito).</summary>
    private static string PgDumpMajorInstalado()
    {
        var (exitCode, stdout, _) = EjecutorBash.Ejecutar("pg_dump --version | grep -oE '[0-9]+' | head -1");
        Assert.Equal(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(stdout),
            "No se pudo determinar la versión de pg_dump instalada; el guardián de paridad " +
            "(Decisión 13) necesita pg_dump presente en la máquina de test.");
        return stdout;
    }

    [Fact]
    public void Preflight_PuertoDeApiOcupado_EsAvisoNoBloqueante()
    {
        var puertoApi = PuertoLibre();
        var puertoPg = PuertoLibre();
        var listener = new TcpListener(IPAddress.Any, puertoApi);
        listener.Start();
        try
        {
            var (exitCode, stdout) = EjecutarPreflight(puertoApi, puertoPg);
            Assert.Contains("OCUPADO", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Preflight_PuertoDePostgresOcupadoPorAlgoQueNoEsStockappPg_EsBloqueante()
    {
        var puertoApi = PuertoLibre();
        var puertoPg = PuertoLibre();
        var listener = new TcpListener(IPAddress.Any, puertoPg);
        listener.Start();
        try
        {
            var (exitCode, stdout) = EjecutarPreflight(puertoApi, puertoPg);
            Assert.Contains("OCUPADO por algo que no es stockapp-pg", stdout);
            Assert.Equal(1, exitCode);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Preflight_SinPuertosOcupados_NoHayBloqueantesDePuertos()
    {
        var puertoApi = PuertoLibre();
        var puertoPg = PuertoLibre();
        var (exitCode, stdout) = EjecutarPreflight(puertoApi, puertoPg);
        Assert.DoesNotContain("OCUPADO", stdout);
        Assert.Equal(0, exitCode);
    }

    // ---------- Guardián 1: el preflight SIEMPRE llega al final e imprime el fingerprint ----------

    /// <summary>
    /// El fingerprint es la razón de ser del preflight (se imprime para emitir la licencia en
    /// paralelo mientras se corre 01-bootstrap.sh). Un chequeo intermedio declarado "no
    /// bloqueante" que aborte el script en silencio (como pasó con la paridad de pg_dump bajo
    /// 'set -o pipefail') es el peor modo de falla posible: nunca llega ni a mostrar el código
    /// de máquina. Formato esperado: 16 grupos de 4 hex mayúsculas separados por guion
    /// (fingerprint.sh: SHA-256 en hex, agrupado de a 4).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PatronFingerprint =
        new(@"[0-9A-F]{4}(-[0-9A-F]{4}){15}");

    [Fact]
    public void Preflight_LlegaAlFinalEImprimeElFingerprint()
    {
        var puertoApi = PuertoLibre();
        var puertoPg = PuertoLibre();
        var (exitCode, stdout) = EjecutarPreflight(puertoApi, puertoPg);
        Assert.Matches(PatronFingerprint, stdout);
        Assert.Contains("PREFLIGHT OK", stdout);
        Assert.Equal(0, exitCode);
    }

    // ---------- Guardián 2: la paridad pg_dump vs. imagen (Decisión 13) funciona cuando hay
    // algo que comparar ----------

    private static string CrearComposeDePrueba(string majorImagen)
    {
        var archivo = Path.GetTempFileName();
        File.WriteAllText(archivo,
            $"services:\n  postgres:\n    image: postgres:{majorImagen}-alpine\n");
        return archivo;
    }

    [Fact]
    public void Preflight_ParidadPgDump_VersionesCoinciden_NoAvisa()
    {
        var majorInstalado = PgDumpMajorInstalado();
        var compose = CrearComposeDePrueba(majorInstalado);
        try
        {
            var puertoApi = PuertoLibre();
            var puertoPg = PuertoLibre();
            var (exitCode, stdout) = EjecutarPreflight(puertoApi, puertoPg, composePostgres: compose);
            Assert.DoesNotContain("pueden no coincidir", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(compose);
        }
    }

    [Fact]
    public void Preflight_ParidadPgDump_VersionesDifieren_AvisaYNoBloquea()
    {
        var majorInstalado = PgDumpMajorInstalado();
        var majorDistinto = (int.Parse(majorInstalado) + 50).ToString();
        var compose = CrearComposeDePrueba(majorDistinto);
        try
        {
            var puertoApi = PuertoLibre();
            var puertoPg = PuertoLibre();
            var (exitCode, stdout) = EjecutarPreflight(puertoApi, puertoPg, composePostgres: compose);
            Assert.Contains(
                $"pg_dump es v{majorInstalado} pero la imagen del compose es postgres:{majorDistinto}-alpine",
                stdout);
            Assert.Contains("pueden no coincidir", stdout);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            File.Delete(compose);
        }
    }
}
