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

public class ProveedoresEndpointTests : ApiTestBase
{
    public ProveedoresEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetProveedores_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProveedores_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProveedores_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Proveedores.Add(new Proveedor { Nombre = "Proveedor Uno", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proveedores = await response.Content.ReadFromJsonAsync<List<Proveedor>>();
        Assert.Contains(proveedores!, p => p.Nombre == "Proveedor Uno");
    }

    [Fact]
    public async Task PostProveedores_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/proveedores",
            new CrearProveedorRequest("Distribuidora XYZ", "011-1234", "xyz@mail.com", "Calle 123", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Proveedores.AnyAsync(p => p.Nombre == "Distribuidora XYZ"));
    }

    [Fact]
    public async Task PostProveedores_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.Proveedores.Add(new Proveedor { Nombre = "Ya Existe", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/proveedores",
            new CrearProveedorRequest("Ya Existe", null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutProveedores_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var proveedor = new Proveedor { Nombre = "Original", Activo = true };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/proveedores/{proveedor.Id}",
            new ModificarProveedorRequest(proveedor.Id, "Modificado", "011-9999", null, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProveedores_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var proveedor = new Proveedor { Nombre = "Para Baja", Activo = true };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/proveedores/{proveedor.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Proveedores.SingleAsync(p => p.Id == proveedor.Id);
        Assert.False(actualizado.Activo);
    }
}
