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

public class OrganismosResponsablesEndpointTests : ApiTestBase
{
    public OrganismosResponsablesEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetOrganismos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/organismos-responsables");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganismos_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/organismos-responsables");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganismos_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/organismos-responsables");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organismos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableDto>>();
        Assert.Contains(organismos!, o => o.Nombre == "Intendencia de Colonia");
    }

    [Fact]
    public async Task PostOrganismos_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/organismos-responsables", new CrearOrganismoResponsableRequest("Municipio de Carmelo"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.OrganismosResponsables.AnyAsync(o => o.Nombre == "Municipio de Carmelo"));
    }

    [Fact]
    public async Task PostOrganismos_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/organismos-responsables", new CrearOrganismoResponsableRequest("Municipio de Carmelo"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostOrganismos_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/organismos-responsables", new CrearOrganismoResponsableRequest("Intendencia de Colonia"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutOrganismos_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var organismo = new OrganismoResponsable { Nombre = "Original", Activo = true };
        ctx.OrganismosResponsables.Add(organismo);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/organismos-responsables/{organismo.Id}", new ModificarOrganismoResponsableRequest("Modificado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOrganismos_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var organismo = new OrganismoResponsable { Nombre = "Para Baja", Activo = true };
        ctx.OrganismosResponsables.Add(organismo);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/organismos-responsables/{organismo.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.OrganismosResponsables.SingleAsync(o => o.Id == organismo.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task GetOrganismosActivos_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Activo", Activo = true });
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Inactivo", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/organismos-responsables/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organismos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableDto>>();
        Assert.Contains(organismos!, o => o.Nombre == "Activo");
        Assert.DoesNotContain(organismos!, o => o.Nombre == "Inactivo");
    }

    /// <summary>Spec 2026-10-30: el catálogo lo consume el formulario de proyecto, así que el listado de activas se
    /// habilita con proyectos.gestionar y no con tareas.gestionar (los permisos de Tareas no dan acceso a Proyectos).</summary>
    [Fact]
    public async Task GetOrganismosActivos_ConOperadorSoloGestionarProyectos_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Activo", Activo = true });
        await ctx.SaveChangesAsync();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "opproyectos", "Secreta123!", new[] { Permisos.VerProyectos, Permisos.GestionarProyectos });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/organismos-responsables/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableDto>>();
        Assert.Contains(lista!, x => x.Nombre == "Activo");
    }

    [Fact]
    public async Task GetOrganismosActivos_ConOperadorSoloGestionarTareas_Devuelve403()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedOperadorConPermisosAsync(
            ctx, "optareas", "Secreta123!", new[] { Permisos.GestionarTareas, Permisos.AdministrarTareas });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/organismos-responsables/activas");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
