using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ReporteValorizacionEndpointTests : ApiTestBase
{
    public ReporteValorizacionEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetReporteValorizacion_ConTokenOperador_Devuelve403()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteValorizacion_ConTokenAdmin_Devuelve200ConValorizacion()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-V1", "Producto Valorizacion Test");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>();
        Assert.Contains(reporte!.Items, i => i.Codigo == "SKU-V1");
    }
}
