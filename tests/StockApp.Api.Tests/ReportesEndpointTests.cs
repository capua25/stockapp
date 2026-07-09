using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ReportesEndpointTests : ApiTestBase
{
    public ReportesEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── GET /reportes/valorizacion ───────────────────────────────────────────

    [Fact]
    public async Task GetValorizacion_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetValorizacion_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetValorizacion_ConTokenAdmin_Devuelve200ConValorizacion()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R1", "Producto Reporte 1");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>();
        Assert.Contains(reporte!.Items, i => i.Codigo == "SKU-R1");
    }

    [Fact]
    public async Task GetProductosReporteValorizacion_RutaVieja_Devuelve405()
    {
        // Desde la Task 10, /productos/{id:int} tiene PUT y DELETE mapeados: ASP.NET Core
        // considera esa forma de ruta como existente (aunque "reporte-valorizacion" no matchee
        // la restricción :int) y responde 405 Method Not Allowed en vez de 404 para un GET.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ── GET /reportes/stock-por-categoria ────────────────────────────────────

    [Fact]
    public async Task GetStockPorCategoria_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R2", "Producto Reporte 2");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/stock-por-categoria");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<StockCategoriaDto>>());
    }

    [Fact]
    public async Task GetStockPorCategoria_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/stock-por-categoria");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /reportes/mas-movidos ────────────────────────────────────────────

    [Fact]
    public async Task GetMasMovidos_ConTokenAdmin_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/mas-movidos?topN=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>());
    }

    [Fact]
    public async Task GetMasMovidos_SinQueryParamTopN_Devuelve200ConDefaultTopN()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/mas-movidos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>();
        Assert.NotNull(resultado);
    }

    // ── GET /reportes/historial-producto/{productoId} ────────────────────────

    [Fact]
    public async Task GetHistorialProducto_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R3", "Producto Reporte 3");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/reportes/historial-producto/{producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>());
    }

    [Fact]
    public async Task GetHistorialProducto_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/historial-producto/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
