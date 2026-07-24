using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ImportacionApiClientTests
{
    [Fact]
    public async Task ListarHistorialAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new ImportacionHistorialDto(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("finanzas/importar/historial", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarHistorialAsync();

        Assert.Single(resultado);
        Assert.Equal("admin", resultado[0].Usuario);
    }
}
