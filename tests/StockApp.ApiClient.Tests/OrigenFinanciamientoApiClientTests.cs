using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class OrigenFinanciamientoApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETOrigenesFinanciamiento_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Presupuesto propio", activo = true },
            new { id = 2, nombre = "Fondo nacional", activo = false },
        }));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var origenes = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, origenes.Count);
        Assert.Equal(1, origenes[0].Id);
        Assert.Equal("Presupuesto propio", origenes[0].Nombre);
        Assert.False(origenes[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETOrigenesFinanciamientoActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var origenes = await client.ListarActivasAsync();

        Assert.Equal("/origenes-financiamiento/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(origenes);
    }

    [Fact]
    public async Task Alta_POSTOrigenesFinanciamiento_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Presupuesto propio\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new OrigenFinanciamiento { Id = 3, Nombre = "Fondo nacional de infraestructura" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Fondo nacional de infraestructura\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEOrigenesFinanciamientoId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un origen de financiamiento con el nombre 'Presupuesto propio'."));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" }));

        Assert.Equal("Ya existe un origen de financiamiento con el nombre 'Presupuesto propio'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Origen de financiamiento 99 no encontrado."));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
