using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ZonasEndpointTests : ApiTestBase
{
    public ZonasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetZonas_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetZonas_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetZonas_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Zonas.Add(new Zona { Nombre = "Centro", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zonas = await response.Content.ReadFromJsonAsync<List<ZonaDto>>();
        Assert.Contains(zonas!, z => z.Nombre == "Centro");
    }

    [Fact]
    public async Task PostZonas_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/zonas", new CrearZonaRequest("Norte"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Zonas.AnyAsync(z => z.Nombre == "Norte"));
    }

    [Fact]
    public async Task PostZonas_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/zonas", new CrearZonaRequest("Norte"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostZonas_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.Zonas.Add(new Zona { Nombre = "Sur", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/zonas", new CrearZonaRequest("Sur"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutZonas_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var zona = new Zona { Nombre = "Original", Activo = true };
        ctx.Zonas.Add(zona);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/zonas/{zona.Id}", new ModificarZonaRequest("Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteZonas_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var zona = new Zona { Nombre = "Para Baja", Activo = true };
        ctx.Zonas.Add(zona);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/zonas/{zona.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.Zonas.SingleAsync(z => z.Id == zona.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetZonasActivas_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Zonas.Add(new Zona { Nombre = "Activa", Activo = true });
        ctx.Zonas.Add(new Zona { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/zonas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zonas = await response.Content.ReadFromJsonAsync<List<ZonaDto>>();
        Assert.Contains(zonas!, z => z.Nombre == "Activa");
        Assert.DoesNotContain(zonas!, z => z.Nombre == "Inactiva");
    }

    [Fact]
    public async Task GetZonasActivas_ConTokenOperador_Devuelve200()
    {
        // Gateada con GestionarTareas, que SÍ está entre los permisos iniciales por defecto de
        // un Operador (AuthorizationService.PermisosInicialesOperador) — a diferencia de
        // GestionarTablasMaestras, que NO lo está. Por eso acá se siembra un Operador real
        // (SeedOperadorConTokenAsync, permisos por defecto) en vez de usar TokenOperador() (un
        // JWT para un usuarioId=2 que no existe en la base: con PoblarPermisosMiddleware
        // puesto, eso da 403 fail-closed sin importar el permiso). Mismo patrón que
        // CategoriasEndpointTests.GetCategoriasActivas_ConTokenOperador_Devuelve200.
        await using var ctx = Factory.CrearContexto();
        ctx.Zonas.Add(new Zona { Nombre = "Activa", Activo = true });
        await ctx.SaveChangesAsync();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/zonas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zonas = await response.Content.ReadFromJsonAsync<List<ZonaDto>>();
        Assert.Contains(zonas!, z => z.Nombre == "Activa");
    }
}
