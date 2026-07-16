using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class FuenteFinanciamientoApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETFinanzasFuentes_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Literal B", activo = true },
            new { id = 2, nombre = "Multas", activo = false },
        }));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var fuentes = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, fuentes.Count);
        Assert.Equal("Literal B", fuentes[0].Nombre);
        Assert.False(fuentes[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETFinanzasFuentesActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var fuentes = await client.ListarActivasAsync();

        Assert.Equal("/finanzas/fuentes/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(fuentes);
    }

    [Fact]
    public async Task Alta_POSTFinanzasFuentes_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new FuenteFinanciamiento { Nombre = "Multas" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Multas\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new FuenteFinanciamiento { Id = 3, Nombre = "Literal C" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Literal C\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasFuentesId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una fuente de financiamiento con el nombre 'Multas'."));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new FuenteFinanciamiento { Nombre = "Multas" }));

        Assert.Equal("Ya existe una fuente de financiamiento con el nombre 'Multas'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Fuente de financiamiento 99 no encontrada."));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
