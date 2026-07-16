using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class IngresoCajaApiClientTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListarTodos_GETFinanzasIngresos_MapeaConFuenteNavegable()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, fecha = Hoy, concepto = "Partida FIGM",
                fuenteFinanciamientoId = 2, fuenteNombre = "Literal B",
                monto = 250000m, activo = true,
            },
        }));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        var ingresos = await client.ListarTodosAsync();

        Assert.Equal("/finanzas/ingresos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var ingreso = Assert.Single(ingresos);
        Assert.Equal("Partida FIGM", ingreso.Concepto);
        Assert.Equal("Literal B", ingreso.FuenteFinanciamiento!.Nombre);
    }

    [Fact]
    public async Task Alta_POSTFinanzasIngresos_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new IngresoCaja
        {
            Fecha = Hoy, Concepto = "Multas", FuenteFinanciamientoId = 2, Monto = 12000m,
        });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Contains("\"concepto\":\"Multas\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new IngresoCaja
        {
            Id = 3, Fecha = Hoy, Concepto = "Editado", FuenteFinanciamientoId = 2, Monto = 100m,
        });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/ingresos/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasIngresosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/ingresos/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Ingreso de caja 99 no encontrado."));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
