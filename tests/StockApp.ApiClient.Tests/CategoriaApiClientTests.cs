using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class CategoriaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETCategorias_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Bebidas", activo = true },
            new { id = 2, nombre = "Limpieza", activo = false },
        }));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var categorias = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, categorias.Count);
        Assert.Equal(1, categorias[0].Id);
        Assert.Equal("Bebidas", categorias[0].Nombre);
        Assert.False(categorias[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETCategoriasActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var categorias = await client.ListarActivasAsync();

        Assert.Equal("/categorias/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(categorias);
    }

    [Fact]
    public async Task Alta_POSTCategorias_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Categoria { Nombre = "Bebidas" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Bebidas\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        // 3a, D1: el id viaja SOLO en la ruta; el body no lo repite.
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Categoria { Id = 3, Nombre = "Bebidas y Licores" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Bebidas y Licores\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETECategoriasId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una categoría con el nombre 'Bebidas'."));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new Categoria { Nombre = "Bebidas" }));

        Assert.Equal("Ya existe una categoría con el nombre 'Bebidas'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Categoría 99 no encontrada."));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
