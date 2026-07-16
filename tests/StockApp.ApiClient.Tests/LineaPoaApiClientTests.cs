using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class LineaPoaApiClientTests
{
    private static LineaPoa LineaConAsignaciones() => new()
    {
        Nombre = "COMPOSTERAS",
        Programa = "Ambiente",
        Ejercicio = 2026,
        Asignaciones =
        {
            new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 100000m },
            new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 50000m },
        },
    };

    [Fact]
    public async Task ListarTodas_GETFinanzasLineasPoa_MapeaLineaYAsignaciones()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, nombre = "COMPOSTERAS", programa = "Ambiente", ejercicio = 2026, activo = true,
                asignaciones = new[]
                {
                    new { id = 10, fuenteFinanciamientoId = 1, fuenteFinanciamientoNombre = "Literal B", monto = 100000m },
                },
            },
        }));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var lineas = await client.ListarTodasAsync();

        Assert.Equal("/finanzas/lineas-poa", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var linea = Assert.Single(lineas);
        Assert.Equal("COMPOSTERAS", linea.Nombre);
        Assert.Equal(2026, linea.Ejercicio);
        var asignacion = Assert.Single(linea.Asignaciones);
        Assert.Equal(1, asignacion.FuenteFinanciamientoId);
        Assert.Equal(100000m, asignacion.Monto);
        // El nombre de la fuente llega mapeado a la nav para que la grilla lo muestre
        Assert.Equal("Literal B", asignacion.FuenteFinanciamiento!.Nombre);
    }

    [Fact]
    public async Task ListarActivas_GETFinanzasLineasPoaActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var lineas = await client.ListarActivasAsync();

        Assert.Equal("/finanzas/lineas-poa/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(lineas);
    }

    [Fact]
    public async Task Alta_POSTFinanzasLineasPoa_EnviaAsignaciones_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 4 }, HttpStatusCode.Created));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(LineaConAsignaciones());

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/lineas-poa", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"COMPOSTERAS\"", fake.UltimoBody);
        Assert.Contains("\"ejercicio\":2026", fake.UltimoBody);
        Assert.Contains("\"fuenteFinanciamientoId\":1", fake.UltimoBody);
        Assert.Contains("\"fuenteFinanciamientoId\":2", fake.UltimoBody);
        Assert.Equal(4, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var linea = LineaConAsignaciones();
        linea.Id = 4;
        await client.ModificarAsync(linea);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/lineas-poa/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasLineasPoaId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/lineas-poa/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "La línea POA debe tener al menos una asignación presupuestal."));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(LineaConAsignaciones()));

        Assert.Equal("La línea POA debe tener al menos una asignación presupuestal.", ex.Message);
    }
}
