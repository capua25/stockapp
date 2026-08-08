using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class BackupsApiClientTests
{
    [Fact]
    public async Task IniciarAsync_POSTBackups_ConLaRutaCorrecta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var client = new BackupsApiClient(TestHttp.CrearCliente(fake));

        await client.IniciarAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/backups", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task IniciarAsync_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya hay un backup en curso."));
        var client = new BackupsApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.IniciarAsync());
        Assert.Equal("Ya hay un backup en curso.", ex.Message);
    }

    [Fact]
    public async Task IniciarAsync_403_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new BackupsApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => client.IniciarAsync());
    }
}
