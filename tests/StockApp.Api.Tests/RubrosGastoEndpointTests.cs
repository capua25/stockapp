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

public class RubrosGastoEndpointTests : ApiTestBase
{
    public RubrosGastoEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetRubros_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/rubros");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRubros_ConTokenOperador_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        // Seed: Admin ocupa Id=1, Operador ocupa Id=2 (coincide con TokenOperador()) — mismo
        // patrón que UsuariosEndpointTests, necesario porque este alta registra auditoría con
        // el UsuarioId del claim del JWT.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/rubros",
            new CrearRubroGastoRequest(3, "Combustibles"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.RubrosGasto.AnyAsync(r => r.Codigo == 3 && r.Nombre == "Combustibles"));
    }

    [Fact]
    public async Task PostRubros_CodigoDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.RubrosGasto.Add(new RubroGasto { Codigo = 5, Nombre = "Papelería", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/rubros",
            new CrearRubroGastoRequest(5, "Otro nombre"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostRubros_CodigoInvalido_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/rubros",
            new CrearRubroGastoRequest(0, "Sin código"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRubros_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var rubro = new RubroGasto { Codigo = 7, Nombre = "Original", Activo = true };
        ctx.RubrosGasto.Add(rubro);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/finanzas/rubros/{rubro.Id}",
            new ModificarRubroGastoRequest(7, "Modificado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.RubrosGasto.SingleAsync(r => r.Id == rubro.Id);
        Assert.Equal("Modificado", actualizado.Nombre);
    }

    [Fact]
    public async Task DeleteRubros_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var rubro = new RubroGasto { Codigo = 8, Nombre = "Para Baja", Activo = true };
        ctx.RubrosGasto.Add(rubro);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/finanzas/rubros/{rubro.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.RubrosGasto.SingleAsync(r => r.Id == rubro.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task GetRubrosActivos_ConTokenOperador_FiltraInactivos()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.RubrosGasto.Add(new RubroGasto { Codigo = 1, Nombre = "Activo", Activo = true });
        ctx.RubrosGasto.Add(new RubroGasto { Codigo = 2, Nombre = "Inactivo", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/finanzas/rubros/activos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rubros = await response.Content.ReadFromJsonAsync<List<RubroGastoDto>>();
        Assert.Contains(rubros!, r => r.Nombre == "Activo");
        Assert.DoesNotContain(rubros!, r => r.Nombre == "Inactivo");
    }
}
