using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class RubroGastoApiClientTests
{
    [Fact]
    public async Task ListarTodos_GETFinanzasRubros_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, codigo = 3, nombre = "Combustibles", activo = true },
            new { id = 2, codigo = 5, nombre = "Papelería", activo = false },
        }));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var rubros = await client.ListarTodosAsync();

        Assert.Equal("/finanzas/rubros", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(2, rubros.Count);
        Assert.Equal(3, rubros[0].Codigo);
        Assert.Equal("Combustibles", rubros[0].Nombre);
        Assert.False(rubros[1].Activo);
    }

    [Fact]
    public async Task ListarActivos_GETFinanzasRubrosActivos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var rubros = await client.ListarActivosAsync();

        Assert.Equal("/finanzas/rubros/activos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(rubros);
    }

    [Fact]
    public async Task Alta_POSTFinanzasRubros_EnviaCodigoYNombre_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 9 }, HttpStatusCode.Created));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" });

        Assert.Equal("/finanzas/rubros", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"codigo\":3", fake.UltimoBody);
        Assert.Contains("\"nombre\":\"Combustibles\"", fake.UltimoBody);
        Assert.Equal(9, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new RubroGasto { Id = 3, Codigo = 4, Nombre = "Lubricantes" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/rubros/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"codigo\":4", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasRubrosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/rubros/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un rubro con el código 3."));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" }));

        Assert.Equal("Ya existe un rubro con el código 3.", ex.Message);
    }
}
