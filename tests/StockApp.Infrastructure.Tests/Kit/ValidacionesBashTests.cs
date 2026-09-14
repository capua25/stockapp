using System.Runtime.CompilerServices;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de deploy/kit/lib/validaciones.sh. La razón de que esta lógica viva en funciones
/// sourceables y no inline en 00-preflight.sh es exactamente esta: así se puede probar acá, en
/// segundos, en vez de únicamente en una VM limpia.
///
/// password_admin_valida espeja ContrasenaValidator (mínimo 8, al menos una letra y un número,
/// src/StockApp.Application/Auth/ContrasenaValidator.cs:9,23-25). Si el kit acepta una
/// contraseña que la API va a rechazar, el bootstrap del admin falla DESPUÉS de que ya se
/// generó el .env y se levantó Postgres -- el peor momento para descubrirlo.
/// </summary>
public class ValidacionesBashTests
{
    private static string RutaLib([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..",
            "deploy", "kit", "lib", "validaciones.sh"));
    }

    private static int Ejecutar(string funcionYArgs)
    {
        var lib = RutaLib();
        Assert.True(File.Exists(lib), $"No se encontró la librería bash en: {lib}");

        // Sin 'set -e': acá nos interesa el exit code de la función, no abortar al primer fallo.
        var (exitCode, _, _) = EjecutorBash.Ejecutar($"source '{lib}'; {funcionYArgs}");
        return exitCode;
    }

    [Theory]
    [InlineData("5080")]
    [InlineData("5433")]
    [InlineData("1")]
    [InlineData("65535")]
    public void EsPuertoValido_AceptaPuertosEnRango(string puerto)
        => Assert.Equal(0, Ejecutar($"es_puerto_valido '{puerto}'"));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("99999")]
    [InlineData("")]
    [InlineData("5080a")]
    [InlineData("-1")]
    [InlineData("50 80")]
    public void EsPuertoValido_RechazaLoQueNoEsUnPuerto(string puerto)
        => Assert.NotEqual(0, Ejecutar($"es_puerto_valido '{puerto}'"));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.1.50")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    // Este caso es el guardián real del "10#": sin forzar base 10, "008" revienta como octal
    // inválido y la función lo rechazaría por error de runtime en vez de aceptarlo como el
    // octeto 8 que es en base 10. Ningún otro caso de esta lista tiene cero a la izquierda,
    // así que sin este InlineData la mutación de sacar el "10#" queda sin detectar (verificado).
    [InlineData("192.168.008.1")]
    public void EsIpv4Valida_AceptaIpsBienFormadas(string ip)
        => Assert.Equal(0, Ejecutar($"es_ipv4_valida '{ip}'"));

    /// <summary>
    /// El caso "008" es el que motivó el 10# de install.sh:191-193: sin forzar base 10, bash
    /// interpreta el cero a la izquierda como octal y "8" no es un dígito octal válido -- error
    /// de runtime ("value too great for base") en vez de un rechazo limpio. Acá tiene que
    /// RECHAZAR con exit != 0, no explotar.
    /// </summary>
    [Theory]
    [InlineData("256.1.1.1")]
    [InlineData("1.1.1")]
    [InlineData("1.1.1.1.1")]
    [InlineData("192.168.1.300")]
    [InlineData("no-es-una-ip")]
    [InlineData("")]
    [InlineData("::1")]
    public void EsIpv4Valida_RechazaLoQueNoEsUnaIpv4(string ip)
        => Assert.NotEqual(0, Ejecutar($"es_ipv4_valida '{ip}'"));

    [Theory]
    [InlineData("admin123")]
    [InlineData("Carmelo2026")]
    [InlineData("a1b2c3d4")]
    public void PasswordAdminValida_AceptaLoQueLaApiAcepta(string pass)
        => Assert.Equal(0, Ejecutar($"password_admin_valida '{pass}'"));

    [Theory]
    [InlineData("corto1")]         // 6 caracteres
    [InlineData("abcdefgh")]       // 8 pero sin número
    [InlineData("12345678")]       // 8 pero sin letra
    [InlineData("")]               // vacía
    [InlineData("       ")]        // solo whitespace
    public void PasswordAdminValida_RechazaLoQueLaApiVaARechazar(string pass)
        => Assert.NotEqual(0, Ejecutar($"password_admin_valida '{pass}'"));
}
