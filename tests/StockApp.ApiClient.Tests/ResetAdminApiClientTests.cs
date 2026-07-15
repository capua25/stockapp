using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient.Tests;

public class ResetAdminApiClientTests
{
    [Fact]
    public async Task SolicitarDesafio_POSTDesafio_DevuelveDto()
    {
        var body = new { desafio = "nonce-1", codigoMaquina = "A3F2-9B41" };
        var fake = new FakeHttpHandler(_ => TestHttp.Json(body));
        var client = new ResetAdminApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.SolicitarDesafioAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/reset-admin/desafio", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal("nonce-1", dto.Desafio);
        Assert.Equal("A3F2-9B41", dto.CodigoMaquina);
    }

    [Fact]
    public async Task Resetear_200_DevuelveExito()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { ok = true }));
        var client = new ResetAdminApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ResetearAsync("token-1", "clave-nueva-9");

        Assert.Equal("/auth/reset-admin", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"token\":\"token-1\"", fake.UltimoBody);
        Assert.Contains("\"nuevaContrasena\":\"clave-nueva-9\"", fake.UltimoBody);
        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task Resetear_400_DevuelveFalloConMotivo()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El desafío expiró. Pedí uno nuevo."));
        var client = new ResetAdminApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ResetearAsync("token-1", "clave-nueva-9");

        Assert.False(resultado.Exito);
        Assert.Equal("El desafío expiró. Pedí uno nuevo.", resultado.Motivo);
    }
}
