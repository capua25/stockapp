using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Api.Tests.Actualizaciones;

/// <summary>
/// El feed de actualizaciones de Velopack que la API sirve en /updates: anónimo, exento del
/// bloqueo por licencia y acotado a la carpeta configurada.
/// </summary>
public class FeedActualizacionesTests : ApiTestBase
{
    private const int TamanoNupkg = 1234;

    public FeedActualizacionesTests(ApiFactory factory) : base(factory) { }

    private void EscribirFeed()
    {
        Directory.CreateDirectory(Factory.DirectorioFeed);
        File.WriteAllText(Path.Combine(Factory.DirectorioFeed, "releases.win.json"), "{\"Assets\":[]}");
        File.WriteAllBytes(
            Path.Combine(Factory.DirectorioFeed, "GestionMunicipal-0.5.0-full.nupkg"),
            new byte[TamanoNupkg]);
        File.WriteAllText(Path.Combine(Factory.DirectorioFeed, "releases.win.json.tmp"), "{\"Assets\":[]}");
    }

    private void Bloquear()
        => Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false;

    [Fact]
    public async Task FeedJson_SinToken_Devuelve200ConJsonYNoCache()
    {
        EscribirFeed();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/updates/releases.win.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Nupkg_SinToken_Devuelve200ConOctetStream()
    {
        EscribirFeed();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/updates/GestionMunicipal-0.5.0-full.nupkg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(TamanoNupkg, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task LicenciaBloqueada_FeedJson_Devuelve200()
    {
        EscribirFeed();
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/updates/releases.win.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/updatesX/releases.win.json")]
    [InlineData("/productos")]
    public async Task LicenciaBloqueada_RutasFueraDelFeed_Siguen423(string ruta)
    {
        EscribirFeed();
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync(ruta);

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task RaizDelFeed_Devuelve404SinListado()
    {
        EscribirFeed();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/updates/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArchivoTmp_Devuelve404()
    {
        EscribirFeed();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/updates/releases.win.json.tmp");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/updates/..%2Ffuera.json")]
    [InlineData("/updates/%2e%2e%2Ffuera.json")]
    [InlineData("/updates/..%5Cfuera.json")]
    public async Task PathTraversal_NoSirveNadaFueraDeLaCarpeta(string ruta)
    {
        EscribirFeed();
        var fuera = Path.Combine(Directory.GetParent(Factory.DirectorioFeed)!.FullName, "fuera.json");
        await File.WriteAllTextAsync(fuera, "\"SECRETO-FUERA\"");
        try
        {
            var client = Factory.CreateClient();

            var response = await client.GetAsync(ruta);
            var cuerpo = await response.Content.ReadAsStringAsync();

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("SECRETO-FUERA", cuerpo);
        }
        finally
        {
            File.Delete(fuera);
        }
    }

    [Fact]
    public async Task CarpetaImposible_LaApiArrancaYElFeedDa404()
    {
        // Un ARCHIVO regular donde debería haber una carpeta padre: CreateDirectory falla.
        var archivo = Path.Combine(Path.GetTempPath(), "StockAppApiTestsFeedImposible_" + Guid.NewGuid());
        await File.WriteAllTextAsync(archivo, "no soy una carpeta");
        try
        {
            await using var factoryImposible = Factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Actualizaciones:Directorio"] = Path.Combine(archivo, "updates"),
                    })));
            var client = factoryImposible.CreateClient();

            var raiz = await client.GetAsync("/");
            var feed = await client.GetAsync("/updates/releases.win.json");

            Assert.Equal(HttpStatusCode.OK, raiz.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, feed.StatusCode);
        }
        finally
        {
            File.Delete(archivo);
        }
    }
}
