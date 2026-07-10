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

public class UnidadesMedidaEndpointTests : ApiTestBase
{
    public UnidadesMedidaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetUnidadesMedida_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUnidadesMedida_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUnidadesMedida_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedidaDto>>();
        Assert.Contains(unidades!, u => u.Nombre == "Kilo");
    }

    [Fact]
    public async Task PostUnidadesMedida_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/unidades-medida", new CrearUnidadMedidaRequest("Metro", "m"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // No existe GET /unidades-medida/{id}: Location debe venir null, no una ruta rota.
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.UnidadesMedida.AnyAsync(u => u.Nombre == "Metro"));
    }

    [Fact]
    public async Task PostUnidadesMedida_AbreviaturaDuplicada_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/unidades-medida", new CrearUnidadMedidaRequest("Kilogramo", "kg"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutUnidadesMedida_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var unidad = new UnidadMedida { Nombre = "Original", Abreviatura = "or", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/unidades-medida/{unidad.Id}",
            new ModificarUnidadMedidaRequest("Modificada", "mo"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUnidadesMedida_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var unidad = new UnidadMedida { Nombre = "Para Baja", Abreviatura = "pb", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/unidades-medida/{unidad.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.UnidadesMedida.SingleAsync(u => u.Id == unidad.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetUnidadesMedidaActivas_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Activa", Abreviatura = "ac", Activo = true });
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Inactiva", Abreviatura = "in", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/unidades-medida/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedidaDto>>();
        Assert.Contains(unidades!, u => u.Nombre == "Activa");
        Assert.DoesNotContain(unidades!, u => u.Nombre == "Inactiva");
    }

    // ── POST /unidades-medida/garantizar-por-defecto (Fase 3a, D6) ──────────

    [Fact]
    public async Task PostGarantizarPorDefecto_SinUnidadPrevia_CreaLaUnidadPorDefecto()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<UnidadMedidaDto>();
        Assert.Equal("Unidad", dto!.Nombre);

        await using var verificacion = Factory.CrearContexto();
        Assert.Equal(1, await verificacion.UnidadesMedida.CountAsync(u => u.Nombre == "Unidad"));
    }

    [Fact]
    public async Task PostGarantizarPorDefecto_LlamadoDosVeces_NoDuplica()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var r1 = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);
        var r2 = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        var dto1 = await r1.Content.ReadFromJsonAsync<UnidadMedidaDto>();
        var dto2 = await r2.Content.ReadFromJsonAsync<UnidadMedidaDto>();
        Assert.Equal(dto1!.Id, dto2!.Id);

        await using var verificacion = Factory.CrearContexto();
        Assert.Equal(1, await verificacion.UnidadesMedida.CountAsync(u => u.Nombre == "Unidad"));
    }

    [Fact]
    public async Task PostGarantizarPorDefecto_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        var tokenOperador = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(operador.Id, RolUsuario.Operador);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenOperador);

        var response = await client.PostAsync("/unidades-medida/garantizar-por-defecto", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
