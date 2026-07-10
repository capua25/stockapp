using System.Net;
using System.Net.Http.Json;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class LoginEndpointTests : ApiTestBase
{
    public LoginEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Login_ConCredencialesValidas_Devuelve200ConTokenYUsuario()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin.test", "Secreta123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal("admin.test", body.Usuario.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, body.Usuario.Rol);
    }

    [Fact]
    public async Task Login_ConCredencialesInvalidas_Devuelve401()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin.test", "ContraseñaIncorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ConBodyVacio_Devuelve400()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
