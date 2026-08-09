using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Alertas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ConfiguracionAlertasEndpointTests : ApiTestBase
{
    public ConfiguracionAlertasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteCon(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Get_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/configuracion/alertas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).GetAsync("/configuracion/alertas");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ConTokenAdmin_Devuelve200ConElEstado()
    {
        var response = await ClienteCon(TokenAdmin()).GetAsync("/configuracion/alertas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ConfiguracionAlertasDto>();
        Assert.NotNull(dto);
        Assert.False(dto!.Habilitado);
    }

    [Fact]
    public async Task Put_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = "https://hc-ping.com/a", Habilitado = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador())
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = "https://hc-ping.com/a", Habilitado = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_UrlHttp_Devuelve400()
    {
        var response = await ClienteCon(TokenAdmin())
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = "http://hc-ping.com/a", Habilitado = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_HabilitadoSinUrl_Devuelve400()
    {
        var response = await ClienteCon(TokenAdmin())
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = (string?)null, Habilitado = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UrlValida_Devuelve200YElGetPosteriorLaDevuelve()
    {
        // El token de Admin usa Id=1 (GenerarToken(1, ...)), pero ApiTestBase.LimpiarTablas()
        // trunca "Usuarios" con CASCADE antes de cada test: sin este seed no existe ninguna
        // fila y ServicioConfiguracionAlertas.GuardarAsync viola la FK Restrict
        // ConfiguracionAlertas.ActualizadoPorUsuarioId -> Usuarios.Id (mismo patrón que
        // BackupsEndpointTests.PostBackups_ConTokenAdmin_*).
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = ClienteCon(TokenAdmin());

        var put = await client.PutAsJsonAsync(
            "/configuracion/alertas", new { UrlWebhook = "https://hc-ping.com/abc", Habilitado = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var dto = await (await client.GetAsync("/configuracion/alertas"))
            .Content.ReadFromJsonAsync<ConfiguracionAlertasDto>();
        Assert.Equal("https://hc-ping.com/abc", dto!.UrlWebhook);
        Assert.True(dto.Habilitado);
    }

    [Fact]
    public async Task Probar_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).PostAsync("/configuracion/alertas/probar", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Probar_SinUrlConfigurada_Devuelve200ConExitosoFalse()
    {
        var response = await ClienteCon(TokenAdmin()).PostAsync("/configuracion/alertas/probar", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ResultadoPruebaAlertaDto>();
        Assert.False(dto!.Exitoso);
    }
}
