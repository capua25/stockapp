using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public record PermisosPropiosResponseTest(List<string> Permisos);

public class AuthEndpointPermisosTests : ApiTestBase
{
    public AuthEndpointPermisosTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    [Fact]
    public async Task GetPermisos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/auth/permisos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPermisos_Admin_DevuelveLos11Configurables()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/auth/permisos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Equal(11, body!.Permisos.Count);
        Assert.Contains(Permisos.VerFinanzas, body.Permisos);
        Assert.DoesNotContain(Permisos.GestionarUsuarios, body.Permisos);
    }

    [Fact]
    public async Task GetPermisos_Operador_DevuelveSoloLosConcedidos()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.permisos", "Secreta123!", RolUsuario.Operador);
        // IProveedorPermisos está registrado Scoped en la collection "Api" (ver ApiFactory,
        // commit b324f52) para aislar la cache entre tests — no se puede resolver desde el
        // root provider (Factory.Services) directamente, hace falta un scope propio. Mismo
        // patrón que PoblarPermisosMiddlewareTests.
        using (var scope = Factory.Services.CreateScope())
        {
            var proveedor = scope.ServiceProvider.GetRequiredService<IProveedorPermisos>();
            await proveedor.GuardarAsync(operador.Id, new[] { Permisos.VerFinanzas, Permisos.GestionarProductos });
        }
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/auth/permisos");

        var body = await response.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Equal(2, body!.Permisos.Count);
        Assert.Contains(Permisos.VerFinanzas, body.Permisos);
        Assert.Contains(Permisos.GestionarProductos, body.Permisos);
    }
}
