using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Application.Finanzas;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class FuentesFinanciamientoEndpointTests : ApiTestBase
{
    public FuentesFinanciamientoEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetFuentes_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/fuentes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFuentes_ConTokenOperador_Devuelve200()
    {
        // Spec Finanzas §9: GestionarMaestrosFinanzas lo tienen Admin Y Operador por
        // ahora — no hay caso 403 por rol para estos endpoints.
        await using var ctx = Factory.CrearContexto();
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Literal B", Activo = true });
        await ctx.SaveChangesAsync();
        // Operador real (no el "2" inventado de TokenOperador()): con PoblarPermisosMiddleware
        // puesto, un JWT para un usuarioId que no existe en la base da 403 fail-closed.
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/finanzas/fuentes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fuentes = await response.Content.ReadFromJsonAsync<List<FuenteFinanciamientoDto>>();
        Assert.Contains(fuentes!, f => f.Nombre == "Literal B");
    }

    [Fact]
    public async Task PostFuentes_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/fuentes",
            new CrearFuenteFinanciamientoRequest("Multas", Caja.Local));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location); // no existe GET /finanzas/fuentes/{id}

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.FuentesFinanciamiento.AnyAsync(f => f.Nombre == "Multas"));
    }

    [Fact]
    public async Task PostFuentes_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Literal C", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/fuentes",
            new CrearFuenteFinanciamientoRequest("Literal C", Caja.Local));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutFuentes_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = new FuenteFinanciamiento { Nombre = "Original", Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/finanzas/fuentes/{fuente.Id}",
            new ModificarFuenteFinanciamientoRequest("Modificada", Caja.Local));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFuentes_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = new FuenteFinanciamiento { Nombre = "Para Baja", Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/finanzas/fuentes/{fuente.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.FuentesFinanciamiento.SingleAsync(f => f.Id == fuente.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetFuentesActivas_ConTokenOperador_FiltraInactivas()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Activa", Activo = true });
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();
        // Operador real (no el "2" inventado de TokenOperador()): con PoblarPermisosMiddleware
        // puesto, un JWT para un usuarioId que no existe en la base da 403 fail-closed.
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/finanzas/fuentes/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fuentes = await response.Content.ReadFromJsonAsync<List<FuenteFinanciamientoDto>>();
        Assert.Contains(fuentes!, f => f.Nombre == "Activa");
        Assert.DoesNotContain(fuentes!, f => f.Nombre == "Inactiva");
    }

    [Fact]
    public async Task Post_SinCaja_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/fuentes",
            new CrearFuenteFinanciamientoRequest("Sin caja", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(FuenteFinanciamientoService.MensajeCajaObligatoria, doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Post_ConCaja1_ElListadoLaDevuelveComoNumero()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var alta = await client.PostAsJsonAsync("/finanzas/fuentes",
            new CrearFuenteFinanciamientoRequest("Literal POA", Caja.Poa));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);

        var listado = await client.GetAsync("/finanzas/fuentes");
        using var doc = JsonDocument.Parse(await listado.Content.ReadAsStringAsync());
        var fuente = doc.RootElement.EnumerateArray().Single(f => f.GetProperty("nombre").GetString() == "Literal POA");
        var caja = fuente.GetProperty("caja");
        Assert.Equal(JsonValueKind.Number, caja.ValueKind);
        Assert.Equal(1, caja.GetInt32());
    }

    [Fact]
    public async Task Put_CambiaLaCajaDeUnaFuenteConPagos_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = new FuenteFinanciamiento { Nombre = "Con ingresos", Activo = true, Caja = Caja.Local };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();
        ctx.IngresosCaja.Add(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Partida", FuenteFinanciamientoId = fuente.Id, Monto = 10m,
        });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/finanzas/fuentes/{fuente.Id}",
            new ModificarFuenteFinanciamientoRequest("Con ingresos", Caja.Poa));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        Assert.Equal(Caja.Local, (await verificacion.FuentesFinanciamiento.SingleAsync(f => f.Id == fuente.Id)).Caja);
    }
}
