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

    [Fact]
    public async Task ProbarAsync_ConUrl_LaViajaEnElBody()
    {
        // Fix IMPORTANTE (I2): el desktop manda la URL que el usuario tiene en pantalla para que
        // el servidor pruebe ESA y no la guardada. Se verifica el contrato serializado completo,
        // mismo criterio que GuardarAsync_EnviaPutConElBody.
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResultadoPruebaAlertaDto(true, 200, "ok")),
        });
        var client = new ConfiguracionAlertasApiClient(TestHttp.CrearCliente(fake));

        await client.ProbarAsync("https://hc-ping.com/nueva");

        Assert.Contains("\"urlWebhook\":\"https://hc-ping.com/nueva\"", fake.UltimoBody);
    }

    [Fact]
    public async Task ProbarAsync_ElServidorReportaFallo_DevuelveElStatusCodeYExitosoFalse()
    {
        // El 200 HTTP es del endpoint (el resultado de la prueba ES la respuesta); el fallo real
        // viaja adentro del DTO. Sin este test, un cliente que descartara Exitoso/StatusCode
        // pasaría desapercibido.
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResultadoPruebaAlertaDto(false, 404, "El webhook respondió 404.")),
        });
        var client = new ConfiguracionAlertasApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ProbarAsync("https://hc-ping.com/typo");

        Assert.False(resultado.Exitoso);
        Assert.Equal(404, resultado.StatusCode);
    }
}
