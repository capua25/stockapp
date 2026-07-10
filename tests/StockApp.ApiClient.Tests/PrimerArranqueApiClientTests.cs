// tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class PrimerArranqueApiClientTests
{
    [Fact]
    public async Task RequiereCrearAdmin_GETAuthPrimerArranque_DevuelveElFlag()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { requiereCrearAdmin = true }));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        var requiere = await client.RequiereCrearAdminAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/primer-arranque", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.True(requiere);
    }

    [Fact]
    public async Task RequiereCrearAdmin_False_DevuelveFalse()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { requiereCrearAdmin = false }));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        Assert.False(await client.RequiereCrearAdminAsync());
    }

    [Fact]
    public async Task CrearAdminInicial_POSTAuthPrimerAdmin_ConElBodyCorrecto()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        await client.CrearAdminInicialAsync("admin", "admin123");

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/primer-admin", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombreUsuario\":\"admin\"", fake.UltimoBody);
        Assert.Contains("\"contrasena\":\"admin123\"", fake.UltimoBody);
    }

    [Fact]
    public async Task CrearAdminInicial_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict,
            "No se puede crear el Admin inicial: ya existen usuarios en la base de datos."));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.CrearAdminInicialAsync("admin", "admin123"));

        Assert.Equal(
            "No se puede crear el Admin inicial: ya existen usuarios en la base de datos.",
            ex.Message);
    }

    [Fact]
    public async Task CrearAdminInicial_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "La contraseña debe tener al menos 6 caracteres."));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CrearAdminInicialAsync("admin", "corta"));
    }
}
