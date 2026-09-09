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

public class OrigenesFinanciamientoEndpointTests : ApiTestBase
{
    public OrigenesFinanciamientoEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetOrigenes_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/origenes-financiamiento");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrigenes_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/origenes-financiamiento");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOrigenes_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/origenes-financiamiento");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoDto>>();
        Assert.Contains(origenes!, o => o.Nombre == "Presupuesto propio");
    }

    [Fact]
    public async Task PostOrigenes_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/origenes-financiamiento", new CrearOrigenFinanciamientoRequest("Fondo nacional"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.OrigenesFinanciamiento.AnyAsync(o => o.Nombre == "Fondo nacional"));
    }

    [Fact]
    public async Task PostOrigenes_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/origenes-financiamiento", new CrearOrigenFinanciamientoRequest("Fondo nacional"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostOrigenes_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Convenio", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/origenes-financiamiento", new CrearOrigenFinanciamientoRequest("Convenio"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutOrigenes_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var origen = new OrigenFinanciamiento { Nombre = "Original", Activo = true };
        ctx.OrigenesFinanciamiento.Add(origen);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/origenes-financiamiento/{origen.Id}", new ModificarOrigenFinanciamientoRequest("Modificado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOrigenes_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var origen = new OrigenFinanciamiento { Nombre = "Para Baja", Activo = true };
        ctx.OrigenesFinanciamiento.Add(origen);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/origenes-financiamiento/{origen.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.OrigenesFinanciamiento.SingleAsync(o => o.Id == origen.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task GetOrigenesActivos_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Activo", Activo = true });
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Inactivo", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/origenes-financiamiento/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoDto>>();
        Assert.Contains(origenes!, o => o.Nombre == "Activo");
        Assert.DoesNotContain(origenes!, o => o.Nombre == "Inactivo");
    }

    [Fact]
    public async Task GetOrigenesActivos_ConTokenOperador_Devuelve200()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonasEndpointTests.
        await using var ctx = Factory.CrearContexto();
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Activo", Activo = true });
        await ctx.SaveChangesAsync();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/origenes-financiamiento/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoDto>>();
        Assert.Contains(origenes!, o => o.Nombre == "Activo");
    }
}
