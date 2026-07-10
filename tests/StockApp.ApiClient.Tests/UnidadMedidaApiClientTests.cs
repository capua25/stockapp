using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class UnidadMedidaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETUnidadesMedida_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Unidad", abreviatura = "u", activo = true },
            new { id = 2, nombre = "Kilo", abreviatura = "kg", activo = false },
        }));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var unidades = await client.ListarTodasAsync();

        Assert.Equal("/unidades-medida", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(2, unidades.Count);
        Assert.Equal("u", unidades[0].Abreviatura);
        Assert.False(unidades[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETUnidadesMedidaActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        await client.ListarActivasAsync();

        Assert.Equal("/unidades-medida/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_POSTUnidadesMedida_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 3 }, HttpStatusCode.Created));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new UnidadMedida { Nombre = "Litro", Abreviatura = "l" });

        Assert.Equal("/unidades-medida", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Litro\"", fake.UltimoBody);
        Assert.Contains("\"abreviatura\":\"l\"", fake.UltimoBody);
        Assert.Equal(3, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new UnidadMedida { Id = 3, Nombre = "Litros", Abreviatura = "lt" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/unidades-medida/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEUnidadesMedidaId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(3);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/unidades-medida/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GarantizarPorDefecto_POSTSinBody_DevuelveLaEntidad()
    {
        // 3a, D6: idempotente — misma unidad "Unidad" en cada llamada, sin duplicar.
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new { id = 1, nombre = "Unidad", abreviatura = "u", activo = true }));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var unidad = await client.GarantizarUnidadPorDefectoAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/unidades-medida/garantizar-por-defecto", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(1, unidad.Id);
        Assert.Equal("Unidad", unidad.Nombre);
    }

    [Fact]
    public async Task Baja_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "La unidad de medida ya está inactiva."));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.BajaLogicaAsync(3));

        Assert.Equal("La unidad de medida ya está inactiva.", ex.Message);
    }
}
