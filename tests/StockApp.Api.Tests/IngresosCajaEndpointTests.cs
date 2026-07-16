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

public class IngresosCajaEndpointTests : ApiTestBase
{
    public IngresosCajaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<int> SeedFuenteAsync()
    {
        await using var ctx = Factory.CrearContexto();
        // Ambos usuarios auditores (1 = Admin, 2 = Operador): la auditoría escribe con
        // el usuarioId del token y su FK Restrict exige que existan.
        if (!await ctx.Usuarios.AnyAsync())
        {
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
            await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        }
        var fuente = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        ctx.Add(fuente);
        await ctx.SaveChangesAsync();
        return fuente.Id;
    }

    [Fact]
    public async Task GetIngresos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/ingresos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIngresos_ConTokenOperador_Crea201()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Partida FIGM julio", fuenteId, 250000m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.IngresosCaja.AnyAsync(i => i.Concepto == "Partida FIGM julio"));
    }

    [Fact]
    public async Task PostIngresos_MontoNoPositivo_Devuelve400()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Inválido", fuenteId, 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetIngresos_DevuelveConNombreDeFuente()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenOperador());
        await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Multas junio", fuenteId, 12000m));

        var response = await client.GetAsync("/finanzas/ingresos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ingresos = await response.Content.ReadFromJsonAsync<List<IngresoCajaDto>>();
        var ingreso = ingresos!.First(i => i.Concepto == "Multas junio");
        Assert.NotNull(ingreso.FuenteNombre);
        Assert.Equal(12000m, ingreso.Monto);
    }

    [Fact]
    public async Task PutIngresos_Modifica200()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Original", fuenteId, 100m));
        await using var ctx = Factory.CrearContexto();
        var id = await ctx.IngresosCaja.Where(i => i.Concepto == "Original")
            .Select(i => i.Id).SingleAsync();

        var response = await client.PutAsJsonAsync($"/finanzas/ingresos/{id}",
            new ModificarIngresoCajaRequest(DateTime.UtcNow, "Editado", fuenteId, 200m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        var ingreso = await verificacion.IngresosCaja.SingleAsync(i => i.Id == id);
        Assert.Equal("Editado", ingreso.Concepto);
        Assert.Equal(200m, ingreso.Monto);
    }

    [Fact]
    public async Task DeleteIngresos_HaceBajaLogica_YRepetido409()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Para baja", fuenteId, 100m));
        await using var ctx = Factory.CrearContexto();
        var id = await ctx.IngresosCaja.Where(i => i.Concepto == "Para baja")
            .Select(i => i.Id).SingleAsync();

        var primera = await client.DeleteAsync($"/finanzas/ingresos/{id}");
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.False((await verificacion.IngresosCaja.SingleAsync(i => i.Id == id)).Activo);

        var segunda = await client.DeleteAsync($"/finanzas/ingresos/{id}");
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }
}
