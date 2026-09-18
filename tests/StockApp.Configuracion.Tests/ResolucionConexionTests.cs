using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using StockApp.Configuracion;
using Xunit;

namespace StockApp.Configuracion.Tests;

/// <summary>
/// Bug 2026-09-18: tools/StockApp.Configurador solo leía conexion.json (ConexionConfigStore) y
/// caía directo a ConexionDefaults.UrlPorDefecto si no existía, salteándose por completo el
/// appsettings.json del directorio de instalación. StockApp.Presentation, en cambio, ya
/// resolvía con precedencia de tres niveles (conexion.json > appsettings.json > default). En
/// una instalación fresca (sin conexion.json) los dos binarios mostraban URLs distintas.
///
/// ResolucionConexion es ahora el ÚNICO lugar que implementa esa cadena. App.axaml.cs
/// (StockApp.Presentation.ConstruirConfiguracion/ResolverApiBaseUrl) y
/// ConfiguradorViewModel delegan acá — no las reimplementan. Estos tests son la fuente de
/// verdad de la precedencia; ResolucionApiBaseUrlTests (StockApp.Presentation.Tests) y
/// ConfiguradorViewModelTests solo verifican que cada consumidor llama a esta función.
/// </summary>
public class ResolucionConexionTests
{
    [Fact]
    public void ResolverApiBaseUrl_SinNingunaFuenteConfigurada_DevuelveElUnicoDefault()
    {
        var configuration = new ConfigurationBuilder().Build();

        var baseUrl = ResolucionConexion.ResolverApiBaseUrl(configuration);

        Assert.Equal(ConexionDefaults.UrlPorDefecto, baseUrl);
    }

    [Fact]
    public void ResolverApiBaseUrl_ConValorEnLaConfiguracion_LoDevuelveEnVezDelDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
            {
                ["Api:BaseUrl"] = "http://192.168.1.50:5080",
            })
            .Build();

        var baseUrl = ResolucionConexion.ResolverApiBaseUrl(configuration);

        Assert.Equal("http://192.168.1.50:5080", baseUrl);
    }

    [Fact]
    public void ConstruirConfiguracion_ArchivoDeConexionEscritoPorElConfigurador_GanaAlAppsettingsDeFabrica()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "resolucion-precedencia-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettings = Path.Combine(carpeta, "appsettings.json");
            File.WriteAllText(rutaAppsettings, "{\"Api\":{\"BaseUrl\":\"http://localhost:5043\"}}");

            var rutaConexion = Path.Combine(carpeta, "conexion.json");
            ConexionConfigStore.Guardar("http://192.168.1.50:5080", rutaConexion);

            var configuration = ResolucionConexion.ConstruirConfiguracion(rutaAppsettings, rutaConexion);

            Assert.Equal("http://192.168.1.50:5080", ResolucionConexion.ResolverApiBaseUrl(configuration));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void ConstruirConfiguracion_SinArchivoDeConexion_CaeAlAppsettingsDeFabrica()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "resolucion-fabrica-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettings = Path.Combine(carpeta, "appsettings.json");
            File.WriteAllText(rutaAppsettings, "{\"Api\":{\"BaseUrl\":\"https://stockapp.capuanomartin.dev:8080\"}}");
            var rutaConexionInexistente = Path.Combine(carpeta, "conexion.json");

            var configuration = ResolucionConexion.ConstruirConfiguracion(rutaAppsettings, rutaConexionInexistente);

            Assert.Equal("https://stockapp.capuanomartin.dev:8080", ResolucionConexion.ResolverApiBaseUrl(configuration));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void ConstruirConfiguracion_SinNingunaFuente_ResuelveAlUnicoDefaultHardcodeado()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "resolucion-sin-nada-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettingsInexistente = Path.Combine(carpeta, "appsettings.json");
            var rutaConexionInexistente = Path.Combine(carpeta, "conexion.json");

            var configuration = ResolucionConexion.ConstruirConfiguracion(rutaAppsettingsInexistente, rutaConexionInexistente);

            Assert.Equal(ConexionDefaults.UrlPorDefecto, ResolucionConexion.ResolverApiBaseUrl(configuration));
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }

    [Fact]
    public void ResolverUrlInicial_AtajoDeUnSoloPaso_DevuelveLoMismoQueLosDosPasos()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "resolucion-atajo-" + Guid.NewGuid());
        Directory.CreateDirectory(carpeta);
        try
        {
            var rutaAppsettings = Path.Combine(carpeta, "appsettings.json");
            File.WriteAllText(rutaAppsettings, "{\"Api\":{\"BaseUrl\":\"https://stockapp.capuanomartin.dev:8080\"}}");
            var rutaConexionInexistente = Path.Combine(carpeta, "conexion.json");

            var atajo = ResolucionConexion.ResolverUrlInicial(rutaAppsettings, rutaConexionInexistente);

            Assert.Equal("https://stockapp.capuanomartin.dev:8080", atajo);
        }
        finally
        {
            Directory.Delete(carpeta, recursive: true);
        }
    }
}
