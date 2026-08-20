using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using StockApp.Configuracion;

namespace StockApp.Configuracion.Tests;

/// <summary>
/// Ata ConexionDefaults.UrlPorDefecto (el único fallback hardcodeado que usa
/// App.ResolverApiBaseUrl cuando NI conexion.json NI appsettings.json están disponibles) con
/// el appsettings.json de FÁBRICA del desktop (src/StockApp.Presentation/appsettings.json).
///
/// Round 1 de esta feature unificó el LUGAR (un solo default, en un solo archivo) pero no
/// verificó el VALOR contra la fábrica: quedó en "http://localhost:5000" mientras
/// appsettings.json dice "http://localhost:5043" — el mismo bug original (dos valores
/// distintos para lo mismo), solo que ahora centralizado en un lugar en vez de duplicado en
/// dos. Puerto real verificado en src/StockApp.Api/Properties/launchSettings.json (ambos
/// profiles "http" y "https" escuchan en :5043) — appsettings.json de fábrica ya tenía el
/// valor correcto, ConexionDefaults tenía el equivocado.
///
/// Este test no valida un número mágico compilado a mano: LEE el appsettings.json real del
/// repo (no el copiado a bin/) y compara contra la constante. Si alguien cambia uno sin el
/// otro, este test revienta.
/// </summary>
public class ConexionDefaultsTests
{
    private static string RutaAppsettingsDeFabrica([CallerFilePath] string archivoDeEsteTest = "")
    {
        var dirDeTests = Path.GetDirectoryName(archivoDeEsteTest)!;
        return Path.GetFullPath(Path.Combine(dirDeTests, "..", "..", "src",
            "StockApp.Presentation", "appsettings.json"));
    }

    [Fact]
    public void UrlPorDefecto_CoincideConElApiBaseUrlDeAppsettingsDeFabrica()
    {
        var ruta = RutaAppsettingsDeFabrica();
        Assert.True(File.Exists(ruta), $"No se encontró el appsettings.json de fábrica en: {ruta}");

        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        var baseUrlDeFabrica = doc.RootElement.GetProperty("Api").GetProperty("BaseUrl").GetString();

        Assert.Equal(ConexionDefaults.UrlPorDefecto, baseUrlDeFabrica);
    }
}
