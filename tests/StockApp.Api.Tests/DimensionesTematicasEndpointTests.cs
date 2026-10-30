using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class DimensionesTematicasEndpointTests : ApiTestBase
{
    public DimensionesTematicasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetDimensiones_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/dimensiones-tematicas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDimensiones_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/dimensiones-tematicas");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDimensiones_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/dimensiones-tematicas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensiones = await response.Content.ReadFromJsonAsync<List<DimensionTematicaDto>>();
        Assert.Contains(dimensiones!, d => d.Nombre == "Infraestructura");
    }

    [Fact]
    public async Task PostDimensiones_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/dimensiones-tematicas", new CrearDimensionTematicaRequest("Tránsito"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.DimensionesTematicas.AnyAsync(d => d.Nombre == "Tránsito"));
    }

    [Fact]
    public async Task PostDimensiones_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/dimensiones-tematicas", new CrearDimensionTematicaRequest("Tránsito"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostDimensiones_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Ambiente", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/dimensiones-tematicas", new CrearDimensionTematicaRequest("Ambiente"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutDimensiones_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var dimension = new DimensionTematica { Nombre = "Original", Activo = true };
        ctx.DimensionesTematicas.Add(dimension);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/dimensiones-tematicas/{dimension.Id}", new ModificarDimensionTematicaRequest("Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDimensiones_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var dimension = new DimensionTematica { Nombre = "Para Baja", Activo = true };
        ctx.DimensionesTematicas.Add(dimension);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/dimensiones-tematicas/{dimension.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.DimensionesTematicas.SingleAsync(d => d.Id == dimension.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetDimensionesActivas_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Activa", Activo = true });
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/dimensiones-tematicas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensiones = await response.Content.ReadFromJsonAsync<List<DimensionTematicaDto>>();
        Assert.Contains(dimensiones!, d => d.Nombre == "Activa");
        Assert.DoesNotContain(dimensiones!, d => d.Nombre == "Inactiva");
    }

    /// <summary>Spec 2026-10-30: el catálogo lo consume el formulario de proyecto, así que el listado de activas se
    /// habilita con proyectos.gestionar y no con tareas.gestionar (los permisos de Tareas no dan acceso a Proyectos).</summary>
    [Fact]
    public async Task GetDimensionesActivas_ConOperadorSoloGestionarProyectos_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Activa", Activo = true });
        await ctx.SaveChangesAsync();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "opproyectos", "Secreta123!", new[] { Permisos.VerProyectos, Permisos.GestionarProyectos });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/dimensiones-tematicas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<DimensionTematicaDto>>();
        Assert.Contains(lista!, x => x.Nombre == "Activa");
    }

    [Fact]
    public async Task GetDimensionesActivas_ConOperadorSoloGestionarTareas_Devuelve403()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "optareas", "Secreta123!", new[] { Permisos.GestionarTareas, Permisos.AdministrarTareas });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/dimensiones-tematicas/activas");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
