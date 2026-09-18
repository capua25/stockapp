using System.Runtime.CompilerServices;
using System.Text.Json;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Kit;

/// <summary>
/// Guardianes de build/pack-testers-win.sh y de src/StockApp.Presentation/appsettings.json.
///
/// Contexto (2026-09-18): antes, la URL del VPS se pisaba A MANO en appsettings.json antes de
/// cada build de testers, y ese pisado terminó commiteado -- cualquier dev que corriera
/// `dotnet run` local le pegaba al VPS real en vez de a localhost. El fix mueve la sustitución
/// AL SCRIPT de empaquetado, con la URL de destino como parámetro OBLIGATORIO (sin default:
/// un default acá es exactamente cómo se vuelve a despachar un build apuntando a donde no
/// corresponde). Estos dos guardianes cubren los dos costados del fix:
///   1. El script aborta (exit != 0) si falta --api-base-url.
///   2. El appsettings.json DEL REPO nunca vuelve a tener una URL de producción -- así esto
///      no se cuela de nuevo en un commit sin que ningún test lo note.
///
/// Guardián 1 verificado por mutación (Task del pedido, 2026-09-18): comentar la guardia
/// "if [[ -z "$API_BASE_URL" ]]; then abort ...; fi" en pack-testers-win.sh hace que el script
/// siga de largo sin --api-base-url (falla más abajo, mucho después, con un error de dotnet
/// publish sobre un appsettings.json que ya no tiene sentido tocar) -- con la guardia puesta,
/// este test da rojo inmediato con exit 1 y el mensaje "--api-base-url" antes de tocar dotnet.
/// </summary>
public class PackTestersWinTests
{
    private static string RutaScript([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "build", "pack-testers-win.sh"));
    }

    private static string RutaAppsettingsRepo([CallerFilePath] string archivo = "")
    {
        var dir = Path.GetDirectoryName(archivo)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..",
            "src", "StockApp.Presentation", "appsettings.json"));
    }

    [Fact]
    public void PackTestersWin_SinApiBaseUrl_AbortaAntesDeTocarDotnet()
    {
        var script = RutaScript();
        Assert.True(File.Exists(script), $"No se encontro el script en: {script}");

        // 'dotnet' NO se stubea a proposito: si la guardia del parametro obligatorio esta
        // rota y el script sigue de largo, va a intentar invocar el dotnet REAL del PATH y
        // fallar mucho mas abajo (con un mensaje de dotnet publish, no el nuestro) -- eso es
        // justamente lo que este test tiene que distinguir de un abort correcto y temprano.
        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar($"bash '{script}' 2>&1");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--api-base-url", stdout + stderr);
        Assert.Contains("obligatorio", stdout + stderr);
    }

    [Fact]
    public void PackTestersWin_UrlInvalida_Aborta()
    {
        var script = RutaScript();
        Assert.True(File.Exists(script), $"No se encontro el script en: {script}");

        var (exitCode, stdout, stderr) = EjecutorBash.Ejecutar(
            $"bash '{script}' --api-base-url no-es-una-url 2>&1");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("URL valida", stdout + stderr);
    }

    [Fact]
    public void AppsettingsDelRepo_ApiBaseUrl_ApuntaALocalhostNoAProduccion()
    {
        var ruta = RutaAppsettingsRepo();
        Assert.True(File.Exists(ruta), $"No se encontro el appsettings.json del repo en: {ruta}");

        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        var baseUrlTexto = doc.RootElement.GetProperty("Api").GetProperty("BaseUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(baseUrlTexto),
            "Api.BaseUrl esta vacio en el appsettings.json del repo.");

        var baseUrl = new Uri(baseUrlTexto!);

        // El default de desarrollo tiene que ser localhost -- cualquier otro host (una IP de
        // VPS, un dominio) es exactamente el bug que motivo este guardian: alguien pisa el
        // valor a mano para armar un build y el pisado queda commiteado.
        Assert.Equal("localhost", baseUrl.Host);
        Assert.Equal(Uri.UriSchemeHttp, baseUrl.Scheme);
    }
}
