using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

/// <summary>
/// Rate limiting de los endpoints anónimos de licenciamiento (Fase B hardening).
/// Usa un factory derivado con un límite bajo propio (WithWebHostBuilder) para no
/// interferir con el límite alto configurado en ApiFactory para el resto de la suite.
/// </summary>
public class RateLimitingTests : ApiTestBase
{
    public RateLimitingTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task LicenciaActivar_SuperaElLimiteConfigurado_Devuelve429()
    {
        await using var factoryLimitado = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:Licenciamiento:PermitLimit"] = "2",
                    ["RateLimiting:Licenciamiento:WindowSeconds"] = "60",
                })));
        var client = factoryLimitado.CreateClient();
        var body = new ActivarLicenciaRequest("licencia-invalida");

        var r1 = await client.PostAsJsonAsync("/licencia/activar", body);
        var r2 = await client.PostAsJsonAsync("/licencia/activar", body);
        var r3 = await client.PostAsJsonAsync("/licencia/activar", body);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, r1.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, r2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
    }

    [Fact]
    public async Task LicenciaEstado_NoTieneLimite_NuncaDevuelve429()
    {
        await using var factoryLimitado = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:Licenciamiento:PermitLimit"] = "1",
                    ["RateLimiting:Licenciamiento:WindowSeconds"] = "60",
                })));
        var client = factoryLimitado.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/licencia/estado");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    /// <summary>
    /// F1 (deploy-vps-linux): POST /auth/login no tenía freno alguno antes de este fix —
    /// aunque el acceso productivo sea por túnel SSH, un login sin límite es fuerza bruta
    /// gratis. Política propia "login" (no comparte balde con "licenciamiento", ver
    /// Program.cs), mismo mecanismo de test que LicenciaActivar_SuperaElLimiteConfigurado_
    /// Devuelve429 de arriba.
    /// </summary>
    [Fact]
    public async Task Login_SuperaElLimiteConfigurado_Devuelve429()
    {
        await using var factoryLimitado = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["RateLimiting:Login:PermitLimit"] = "2",
                    ["RateLimiting:Login:WindowSeconds"] = "60",
                })));
        var client = factoryLimitado.CreateClient();
        var body = new LoginRequest("usuario-inexistente", "lo-que-sea");

        var r1 = await client.PostAsJsonAsync("/auth/login", body);
        var r2 = await client.PostAsJsonAsync("/auth/login", body);
        var r3 = await client.PostAsJsonAsync("/auth/login", body);

        Assert.NotEqual(HttpStatusCode.TooManyRequests, r1.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, r2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
    }
}
