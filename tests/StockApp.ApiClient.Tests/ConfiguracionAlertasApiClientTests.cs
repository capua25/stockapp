using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Alertas;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ConfiguracionAlertasApiClientTests
{
    [Fact]
    public async Task ObtenerAsync_PegaAlaRutaCorrectaYDeserializa()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ConfiguracionAlertasDto("https://hc-ping.com/a", true, null)),
        });
        var client = new ConfiguracionAlertasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.EndsWith("configuracion/alertas", fake.UltimaRequest.RequestUri!.ToString());
        Assert.Equal("https://hc-ping.com/a", dto.UrlWebhook);
        Assert.True(dto.Habilitado);
    }

    [Fact]
    public async Task GuardarAsync_EnviaPutConElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ConfiguracionAlertasApiClient(TestHttp.CrearCliente(fake));

        await client.GuardarAsync("https://hc-ping.com/b", habilitado: true);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        // Verifica el contrato serializado completo (nombre de propiedad + valor), no solo que
        // el valor aparezca en algún lado — si mañana se renombra UrlWebhook/Habilitado en el
        // record de body, esto debe romperse en vez de seguir en verde por casualidad.
        Assert.Contains("\"urlWebhook\":\"https://hc-ping.com/b\"", fake.UltimoBody);
        Assert.Contains("\"habilitado\":true", fake.UltimoBody);
    }

    [Fact]
    public async Task ProbarAsync_EnviaPostYDevuelveElResultado()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResultadoPruebaAlertaDto(true, 200, "ok")),
        });
        var client = new ConfiguracionAlertasApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ProbarAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.EndsWith("configuracion/alertas/probar", fake.UltimaRequest.RequestUri!.ToString());
        Assert.True(resultado.Exitoso);
    }
}
