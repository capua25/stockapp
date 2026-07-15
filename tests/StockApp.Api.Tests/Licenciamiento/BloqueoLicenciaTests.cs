using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class BloqueoLicenciaTests : ApiTestBase
{
    public BloqueoLicenciaTests(ApiFactory factory) : base(factory) { }

    private void Bloquear()
        => Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false;

    [Fact]
    public async Task Bloqueada_EndpointNormal_Devuelve423()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(423, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Licencia no activada.", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "El servidor no tiene una licencia válida activada. "
            + "Activá la licencia desde la pantalla de bloqueo del cliente.",
            doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Bloqueada_Login_Devuelve423()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "x", Contrasena = "y" });

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_EstadoDeLicencia_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/licencia/estado");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_DesafioDeReset_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/auth/reset-admin/desafio", content: null);

        // El endpoint de reset se agrega en Task 5; acá sólo importa que el middleware NO lo bloquee.
        // Antes de Task 5 devolverá 404 (ruta inexistente), NO 423.
        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Activada_EndpointNormal_NoDevuelve423()
    {
        // ApiTestBase deja Activada=true por defecto.
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        // Sin token → 401, pero NO 423 (la licencia está activa).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
