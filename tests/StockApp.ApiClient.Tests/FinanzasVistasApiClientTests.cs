using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class FinanzasVistasApiClientTests
{
    [Fact]
    public async Task ObtenerLibroCajaMesAsync_GETConAnioYMes_DeserializaDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            anio = 2026, mes = 7, saldoInicial = 100m, saldoFinal = 200m,
            movimientos = Array.Empty<object>(),
            totalesPorRubro = Array.Empty<object>(),
            totalesPorFuente = Array.Empty<object>(),
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerLibroCajaMesAsync(2026, 7);

        Assert.Equal("/finanzas/libro-caja", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("anio=2026", fake.UltimaRequest.RequestUri.Query);
        Assert.Contains("mes=7", fake.UltimaRequest.RequestUri.Query);
        Assert.Equal(200m, dto.SaldoFinal);
    }

    [Fact]
    public async Task ObtenerLibroCajaAnualAsync_GETSoloConAnio_DeserializaDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            anio = 2026,
            totalesPorMes = Array.Empty<object>(),
            totalesPorRubro = Array.Empty<object>(),
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerLibroCajaAnualAsync(2026);

        Assert.DoesNotContain("mes=", fake.UltimaRequest!.RequestUri!.Query);
        Assert.Equal(2026, dto.Anio);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_GETConEjercicio_DeserializaLista()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                lineaPoaId = 1, nombre = "Rambla", programa = "Obras", ejercicio = 2026,
                presupuesto = 1000m, gastado = 400m, saldo = 600m,
                porcentajeEjecucion = 40m, sobregirada = false,
            },
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var lista = await client.ObtenerControlPoaAsync(2026);

        Assert.Equal("/finanzas/control-poa", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var fila = Assert.Single(lista);
        Assert.Equal("Rambla", fila.Nombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_GETSinParametros_DeserializaDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            vencidas = Array.Empty<object>(),
            aVencer7Dias = Array.Empty<object>(),
            aVencer30Dias = Array.Empty<object>(),
            pagosRecientes = Array.Empty<object>(),
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerCalendarioPagosAsync();

        Assert.Equal("/finanzas/calendario-pagos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(dto.Vencidas);
    }
}
