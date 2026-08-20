using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using StockApp.Configuracion;
using Xunit;

namespace StockApp.Presentation.Tests.Config;

/// <summary>
/// Bug corregido acá: App.axaml.cs tenía DOS defaults distintos para Api:BaseUrl —
/// "5043" en appsettings.json y "http://localhost:5000" hardcodeado por DUPLICADO en
/// App.ConfigurarServicios (HttpClient principal y HttpClient "Descargas"). Si faltaba el
/// appsettings.json (se lee con optional: true) la app caía a un puerto donde no escuchaba
/// nadie. Ahora hay un solo resolvedor (App.ResolverApiBaseUrl) y un solo default
/// (ConexionDefaults.UrlPorDefecto).
///
/// El segundo grupo de tests ata el escritor real (ConexionConfigStore.Guardar, el mismo que
/// usa tools/StockApp.Configurador) con el lector real (App.ConstruirConfiguracion +
/// App.ResolverApiBaseUrl, el mismo que usa la app de escritorio): si algún día la clave o la
/// precedencia se desincronizan entre los dos binarios, este test lo revienta.
/// </summary>
public class ResolucionApiBaseUrlTests
{
    [Fact]
    public void ResolverApiBaseUrl_SinNingunaFuenteConfigurada_DevuelveElUnicoDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var baseUrl = App.ResolverApiBaseUrl(configuration);

        Assert.Equal(ConexionDefaults.UrlPorDefecto, baseUrl);
    }

    [Fact]
    public void ResolverApiBaseUrl_ConValorEnLaConfiguracion_LoDevuelveEnVezDelDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "http://192.168.1.50:5080",
            })
            .Build();

        var baseUrl = App.ResolverApiBaseUrl(configuration);

        Assert.Equal("http://192.168.1.50:5080", baseUrl);
    }

    [Fact]
    public void ConstruirConfiguracion_ArchivoDeConexionEscritoPorElConfigurador_GanaAlAppsettingsDeFabrica()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "config-precedencia-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettings = Path.Combine(carpeta, "appsettings.json");
            File.WriteAllText(rutaAppsettings, "{\"Api\":{\"BaseUrl\":\"http://localhost:5043\"}}");

            var rutaConexion = Path.Combine(carpeta, "conexion.json");
            // Mismo método que usa tools/StockApp.Configurador al presionar "Guardar".
            ConexionConfigStore.Guardar("http://192.168.1.50:5080", rutaConexion);

            var configuration = App.ConstruirConfiguracion(rutaAppsettings, rutaConexion);

            Assert.Equal("http://192.168.1.50:5080", App.ResolverApiBaseUrl(configuration));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void ConstruirConfiguracion_SinArchivoDeConexion_CaeAlAppsettingsDeFabrica()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "config-fabrica-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettings = Path.Combine(carpeta, "appsettings.json");
            File.WriteAllText(rutaAppsettings, "{\"Api\":{\"BaseUrl\":\"http://localhost:5043\"}}");
            var rutaConexionInexistente = Path.Combine(carpeta, "conexion.json");

            var configuration = App.ConstruirConfiguracion(rutaAppsettings, rutaConexionInexistente);

            Assert.Equal("http://localhost:5043", App.ResolverApiBaseUrl(configuration));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void ConstruirConfiguracion_SinNingunaFuente_ResuelveAlUnicoDefaultHardcodeado()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "config-sin-nada-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettingsInexistente = Path.Combine(carpeta, "appsettings.json");
            var rutaConexionInexistente = Path.Combine(carpeta, "conexion.json");

            var configuration = App.ConstruirConfiguracion(rutaAppsettingsInexistente, rutaConexionInexistente);

            Assert.Equal(ConexionDefaults.UrlPorDefecto, App.ResolverApiBaseUrl(configuration));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }
}
