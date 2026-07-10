// tests/StockApp.ApiClient.Tests/ProductoApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ProductoApiClientTests
{
    private static readonly object ProductoJson = new
    {
        id = 1, codigo = "SKU-001", codigoBarras = "7791234567890", nombre = "Agua 2L",
        descripcion = (string?)null, categoriaId = 2, categoriaNombre = "Bebidas",
        proveedorId = (int?)null, unidadMedidaId = 1, unidadMedidaNombre = "Unidad",
        precioCosto = 25.5, precioVenta = 40.0, stockActual = 12.0, stockMinimo = 3.0,
        activo = true, fechaAlta = "2026-07-01T10:00:00Z",
    };

    [Fact]
    public async Task Buscar_SinFiltros_GETProductosSinQuery_DevuelveLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[] { ProductoJson }));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        var productos = await client.BuscarAsync(null, null, null);

        Assert.Equal("/productos", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var p = Assert.Single(productos);
        Assert.Equal("SKU-001", p.Codigo);
        Assert.Equal("Bebidas", p.CategoriaNombre);
        Assert.Equal(25.5m, p.PrecioCosto);
        Assert.Equal(12m, p.StockActual);
    }

    [Fact]
    public async Task Buscar_ConFiltros_ArmaLaQuerySoloConLosPresentes()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BuscarAsync("SKU-001", null, "agua");

        Assert.Equal("/productos?sku=SKU-001&nombre=agua", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task BuscarPorTexto_GETProductosConTexto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BuscarPorTextoAsync("agua con gas");

        Assert.Equal("/productos?texto=agua%20con%20gas", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task BuscarPorTexto_Null_GETProductosSinQuery()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BuscarPorTextoAsync(null);

        Assert.Equal("/productos", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Alta_POSTProductos_SoloLosCamposDelRequest_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 11 }, HttpStatusCode.Created));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Producto
        {
            Codigo = "SKU-002", Nombre = "Yerba 1kg", UnidadMedidaId = 1,
            CategoriaId = 2, PrecioCosto = 100m, PrecioVenta = 150m, StockMinimo = 5m,
            StockActual = 99m, // el stock NO viaja en el alta: lo gobiernan los movimientos
        });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/productos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"codigo\":\"SKU-002\"", fake.UltimoBody);
        Assert.Contains("\"stockMinimo\":5", fake.UltimoBody);
        Assert.DoesNotContain("stockActual", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
        Assert.Equal(11, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Producto
        {
            Id = 11, Codigo = "SKU-002", Nombre = "Yerba 1kg suave", UnidadMedidaId = 1,
        });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/11", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEProductosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(11);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/11", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CambiarPrecio_PUTProductosIdPrecio()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarPrecioAsync(11, 110m, 165m);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/11/precio", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"precioCosto\":110", fake.UltimoBody);
        Assert.Contains("\"precioVenta\":165", fake.UltimoBody);
    }

    [Fact]
    public async Task Modificar_404_LanzaEntidadNoEncontradaConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Producto 99 no encontrado."));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.ModificarAsync(new Producto { Id = 99, Codigo = "X", Nombre = "X", UnidadMedidaId = 1 }));

        Assert.Equal("Producto 99 no encontrado.", ex.Message);
    }
}
