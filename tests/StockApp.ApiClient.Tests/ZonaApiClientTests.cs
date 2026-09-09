using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ZonaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETZonas_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Centro", activo = true },
            new { id = 2, nombre = "Norte", activo = false },
        }));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var zonas = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, zonas.Count);
        Assert.Equal(1, zonas[0].Id);
        Assert.Equal("Centro", zonas[0].Nombre);
        Assert.False(zonas[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETZonasActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var zonas = await client.ListarActivasAsync();

        Assert.Equal("/zonas/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(zonas);
    }

    [Fact]
    public async Task Alta_POSTZonas_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Zona { Nombre = "Centro" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Centro\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Zona { Id = 3, Nombre = "Norte Ampliado" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Norte Ampliado\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEZonasId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una zona con el nombre 'Centro'."));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new Zona { Nombre = "Centro" }));

        Assert.Equal("Ya existe una zona con el nombre 'Centro'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Zona 99 no encontrada."));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
