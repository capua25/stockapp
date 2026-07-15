using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient.Tests;

public class LicenciaApiClientTests
{
    [Fact]
    public async Task ObtenerEstado_GETLicenciaEstado_DevuelveDto()
    {
        var body = new { activada = false, codigoMaquina = "A3F2-9B41" };
        var fake = new FakeHttpHandler(_ => TestHttp.Json(body));
        var client = new LicenciaApiClient(TestHttp.CrearCliente(fake));

        var estado = await client.ObtenerEstadoAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/licencia/estado", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.False(estado.Activada);
        Assert.Equal("A3F2-9B41", estado.CodigoMaquina);
    }

    [Fact]
    public async Task Activar_200_DevuelveExito()
    {
        var body = new { activada = true, codigoMaquina = "A3F2-9B41" };
        var fake = new FakeHttpHandler(_ => TestHttp.Json(body));
        var client = new LicenciaApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ActivarAsync("payload.firma");

        Assert.Equal("/licencia/activar", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"licencia\":\"payload.firma\"", fake.UltimoBody);
        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task Activar_400_DevuelveFalloConMotivo()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "La licencia fue emitida para otra máquina."));
        var client = new LicenciaApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ActivarAsync("payload.firma");

        Assert.False(resultado.Exito);
        Assert.Equal("La licencia fue emitida para otra máquina.", resultado.Motivo);
    }
}
