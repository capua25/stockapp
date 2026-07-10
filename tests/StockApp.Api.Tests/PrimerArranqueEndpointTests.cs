using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class PrimerArranqueEndpointTests : ApiTestBase
{
    public PrimerArranqueEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetPrimerArranque_ServidorVirgen_RequiereCrearAdminTrue()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/auth/primer-arranque");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.True(body!.RequiereCrearAdmin);
    }

    [Fact]
    public async Task PostPrimerAdmin_ServidorVirgen_CreaAdminYDevuelve201()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.inicial", "secreto123"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Usuarios.AnyAsync(
            u => u.NombreUsuario == "admin.inicial" && u.Rol == RolUsuario.Admin));
    }

    [Fact]
    public async Task FlujoCompleto_RequiereAntes_CreaAdmin_YaNoRequiereDespues()
    {
        var client = Factory.CreateClient();

        var antes = await client.GetAsync("/auth/primer-arranque");
        var antesBody = await antes.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.True(antesBody!.RequiereCrearAdmin);

        await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.flujo", "secreto123"));

        var despues = await client.GetAsync("/auth/primer-arranque");
        var despuesBody = await despues.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.False(despuesBody!.RequiereCrearAdmin);
    }

    [Fact]
    public async Task PostPrimerAdmin_LlamadoDeNuevo_Devuelve409()
    {
        var client = Factory.CreateClient();

        await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.uno", "secreto123"));

        var segundo = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("admin.dos", "secreto123"));

        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
    }

    [Fact]
    public async Task PostPrimerAdmin_ConUsuariosExistentes_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.previo", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("otro.admin", "secreto123"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostPrimerAdmin_NombreUsuarioEnBlanco_Devuelve400YNoCreaAdmin()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/primer-admin", new CrearAdminInicialRequest("   ", "secreto123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var despues = await client.GetAsync("/auth/primer-arranque");
        var despuesBody = await despues.Content.ReadFromJsonAsync<PrimerArranqueEstadoResponse>();
        Assert.True(despuesBody!.RequiereCrearAdmin);
    }
}
