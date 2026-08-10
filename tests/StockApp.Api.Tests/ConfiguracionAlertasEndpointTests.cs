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

    /// <summary>
    /// Fix (MENOR M5 del review final): la matriz 401/403 estaba completa para GET y PUT, pero
    /// para POST /probar solo existía el 403. El plan pedía la matriz sobre los tres — y /probar
    /// es justo el que hace que el SERVIDOR emita una petición saliente a una URL provista por el
    /// usuario, así que es el último donde conviene tener un hueco en la matriz de autenticación.
    /// </summary>
    [Fact]
    public async Task Probar_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().PostAsync("/configuracion/alertas/probar", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    [Fact]
    public async Task Probar_ConUrlHttpEnElBody_Devuelve400()
    {
        // Fix IMPORTANTE (I2): la URL de pantalla se valida IGUAL que al guardar. Si esta ruta no
        // validara, "probar sin guardar" sería la puerta de atrás que saltea el gate de https.
        var response = await ClienteCon(TokenAdmin())
            .PostAsJsonAsync("/configuracion/alertas/probar", new { UrlWebhook = "http://hc-ping.com/a" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Probar_ConUrlRelativaEnElBody_Devuelve400()
    {
        var response = await ClienteCon(TokenAdmin())
            .PostAsJsonAsync("/configuracion/alertas/probar", new { UrlWebhook = "hc-ping.com/a" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Probar_ConBodyVacio_UsaLaConfiguracionGuardadaYDevuelve200()
    {
        // El body es opcional: sin URL en pantalla se prueba la guardada (acá, ninguna).
        var response = await ClienteCon(TokenAdmin())
            .PostAsJsonAsync("/configuracion/alertas/probar", new { UrlWebhook = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ResultadoPruebaAlertaDto>();
        Assert.False(dto!.Exitoso);
        Assert.Contains("URL", dto.Mensaje!);
    }
}
