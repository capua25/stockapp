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

public class CategoriasEndpointTests : ApiTestBase
{
    public CategoriasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetCategorias_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCategorias_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCategorias_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Categorias.Add(new Categoria { Nombre = "Bebidas", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<CategoriaDto>>();
        Assert.Contains(categorias!, c => c.Nombre == "Bebidas");
    }

    [Fact]
    public async Task PostCategorias_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/categorias", new CrearCategoriaRequest("Lácteos"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // No existe GET /categorias/{id}: Location debe venir null, no una ruta rota.
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Categorias.AnyAsync(c => c.Nombre == "Lácteos"));
    }

    [Fact]
    public async Task PostCategorias_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/categorias", new CrearCategoriaRequest("Lácteos"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostCategorias_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.Categorias.Add(new Categoria { Nombre = "Carnes", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/categorias", new CrearCategoriaRequest("Carnes"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutCategorias_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var categoria = new Categoria { Nombre = "Original", Activo = true };
        ctx.Categorias.Add(categoria);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/categorias/{categoria.Id}", new ModificarCategoriaRequest("Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategorias_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var categoria = new Categoria { Nombre = "Para Baja", Activo = true };
        ctx.Categorias.Add(categoria);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/categorias/{categoria.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.Categorias.SingleAsync(c => c.Id == categoria.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetCategoriasActivas_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Categorias.Add(new Categoria { Nombre = "Activa", Activo = true });
        ctx.Categorias.Add(new Categoria { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/categorias/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<CategoriaDto>>();
        Assert.Contains(categorias!, c => c.Nombre == "Activa");
        Assert.DoesNotContain(categorias!, c => c.Nombre == "Inactiva");
    }
}
