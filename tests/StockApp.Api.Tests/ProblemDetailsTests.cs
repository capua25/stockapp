using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ProblemDetailsTests : ApiTestBase
{
    public ProblemDetailsTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task SinToken_Devuelve401ComoProblemDetails()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task TokenOperador_EnEndpointSoloAdmin_Devuelve403ComoProblemDetails()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task LoginBodyVacio_Devuelve400ComoProblemDetails()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }
}
