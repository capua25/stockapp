using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;

namespace StockApp.ApiClient.Tests;

public class ReporteStockApiClientTests
{
    [Fact]
    public async Task Valorizacion_GETReportesValorizacion_MapeaItemsYTotales()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            items = new[]
            {
                new
                {
                    productoId = 1, codigo = "SKU-001", nombre = "Agua 2L", categoria = "Bebidas",
                    stockActual = 12.0, precioCosto = 25.5, precioVenta = 40.0,
                    valorCosto = 306.0, valorVenta = 480.0,
                },
            },
            totales = new { totalValorCosto = 306.0, totalValorVenta = 480.0 },
        }));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        var reporte = await client.ObtenerValorizacionAsync();

        Assert.Equal("/reportes/valorizacion", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var item = Assert.Single(reporte.Items);
        Assert.Equal("SKU-001", item.Codigo);
        Assert.Equal(306m, item.ValorCosto);
        Assert.Equal(480m, reporte.Totales.TotalValorVenta);
    }

    [Fact]
    public async Task StockPorCategoria_GETReportesStockPorCategoria()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                categoria = "Bebidas", cantidadProductos = 3, stockTotal = 50.0,
                valorCosto = 1000.0, valorVenta = 1500.0,
            },
        }));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        var resumen = await client.ObtenerStockPorCategoriaAsync();

        Assert.Equal("/reportes/stock-por-categoria", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var fila = Assert.Single(resumen);
        Assert.Equal("Bebidas", fila.Categoria);
        Assert.Equal(3, fila.CantidadProductos);
    }

    [Fact]
    public async Task MasMovidos_GETConFechasYTopN()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerMasMovidosAsync(
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 10), topN: 5);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/mas-movidos?", pathAndQuery);
        Assert.Contains("fechaDesde=2026-07-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("topN=5", pathAndQuery);
    }

    [Fact]
    public async Task MasMovidos_SinFechas_SoloEnviaTopNPorDefecto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerMasMovidosAsync(null, null);

        Assert.Equal("/reportes/mas-movidos?topN=20", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task HistorialPorProducto_GETConElIdEnLaRuta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerHistorialPorProductoAsync(7, new DateTime(2026, 7, 1), null);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/historial-producto/7?", pathAndQuery);
        Assert.Contains("fechaDesde=", pathAndQuery);
        Assert.DoesNotContain("fechaHasta=", pathAndQuery);
    }

    [Fact]
    public async Task Valorizacion_403Operador_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerValorizacionAsync());
    }
}
