using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class DimensionTematicaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETDimensionesTematicas_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Infraestructura", activo = true },
            new { id = 2, nombre = "Tránsito", activo = false },
        }));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var dimensiones = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, dimensiones.Count);
        Assert.Equal(1, dimensiones[0].Id);
        Assert.Equal("Infraestructura", dimensiones[0].Nombre);
        Assert.False(dimensiones[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETDimensionesTematicasActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var dimensiones = await client.ListarActivasAsync();

        Assert.Equal("/dimensiones-tematicas/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(dimensiones);
    }

    [Fact]
    public async Task Alta_POSTDimensionesTematicas_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Infraestructura\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new DimensionTematica { Id = 3, Nombre = "Tránsito y Movilidad" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        // System.Text.Json escapa no-ASCII por default (PostAsJsonAsync/PutAsJsonAsync sin
        // opciones explícitas, igual que el resto de los ~21 ApiClients del repo): "á" viaja
        // como á en el body serializado. Se verifica el JSON tal cual se envía.
        Assert.Contains("\"nombre\":\"Tr\\u00E1nsito y Movilidad\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEDimensionesTematicasId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una dimensión con el nombre 'Infraestructura'."));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" }));

        Assert.Equal("Ya existe una dimensión con el nombre 'Infraestructura'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Dimensión 99 no encontrada."));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
