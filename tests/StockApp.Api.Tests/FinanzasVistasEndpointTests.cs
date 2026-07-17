using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class FinanzasVistasEndpointTests : ApiTestBase
{
    public FinanzasVistasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedUsuarioAdminAsync()
    {
        await using var ctx = Factory.CrearContexto();
        if (!await ctx.Usuarios.AnyAsync())
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetControlPoa_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/control-poa?ejercicio=2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCalendarioPagos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/calendario-pagos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 200 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_ConMes_Devuelve200ConLibroCajaMesDto()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaMesDto>();
        Assert.NotNull(dto);
        Assert.Equal(2026, dto!.Anio);
        Assert.Equal(7, dto.Mes);
    }

    [Fact]
    public async Task GetLibroCaja_SinMes_Devuelve200ConLibroCajaAnualDto()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaAnualDto>();
        Assert.NotNull(dto);
        Assert.Equal(12, dto!.TotalesPorMes.Count);
    }

    [Fact]
    public async Task GetControlPoa_Devuelve200ConListaVacia_SinLineasDelEjercicio()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/control-poa?ejercicio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<List<ControlPoaLineaDto>>();
        Assert.NotNull(dto);
        Assert.Empty(dto!);
    }

    [Fact]
    public async Task GetCalendarioPagos_Devuelve200ConCalendarioVacio()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/calendario-pagos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CalendarioPagosDto>();
        Assert.NotNull(dto);
        Assert.Empty(dto!.Vencidas);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_MesFueraDeRango_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLibroCaja_SinAnio_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetControlPoa_SinEjercicio_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/control-poa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
