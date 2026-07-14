using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class BootstrapAdminSeederIntegracionTests : ApiTestBase
{
    public BootstrapAdminSeederIntegracionTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task SembrarAsync_DbVirgen_CreaAdminQuePuedeLoguearse()
    {
        // La base arranca vacía (TRUNCATE en ApiTestBase). Resolvemos el servicio real
        // (PrimerArranqueService + repo contra el Postgres del contenedor) y sembramos.
        using var scope = Factory.Services.CreateScope();
        var primerArranque = scope.ServiceProvider.GetRequiredService<IPrimerArranqueService>();
        var seeder = new BootstrapAdminSeeder(primerArranque, "admin-seed-test", "clave-seed-123");

        await seeder.SembrarAsync();

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin-seed-test", "clave-seed-123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("admin-seed-test", body!.Usuario.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, body.Usuario.Rol);
    }
}
