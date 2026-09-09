using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class OrganismoResponsableApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETOrganismosResponsables_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Intendencia de Colonia", activo = true },
            new { id = 2, nombre = "Municipio de Carmelo", activo = false },
        }));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var organismos = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, organismos.Count);
        Assert.Equal(1, organismos[0].Id);
        Assert.Equal("Intendencia de Colonia", organismos[0].Nombre);
        Assert.False(organismos[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETOrganismosResponsablesActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var organismos = await client.ListarActivasAsync();

        Assert.Equal("/organismos-responsables/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(organismos);
    }

    [Fact]
    public async Task Alta_POSTOrganismosResponsables_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Intendencia de Colonia\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new OrganismoResponsable { Id = 3, Nombre = "Municipio de Carmelo (Junta Local)" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Municipio de Carmelo (Junta Local)\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEOrganismosResponsablesId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un organismo con el nombre 'Intendencia de Colonia'."));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new OrganismoResponsable { Nombre = "Intendencia de Colonia" }));

        Assert.Equal("Ya existe un organismo con el nombre 'Intendencia de Colonia'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Organismo 99 no encontrado."));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
