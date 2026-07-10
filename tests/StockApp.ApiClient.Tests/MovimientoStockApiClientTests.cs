// tests/StockApp.ApiClient.Tests/MovimientoStockApiClientTests.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class MovimientoStockApiClientTests
{
    private static RegistrarMovimientoDto Salida(decimal cantidad = 8m) => new(
        ProductoId: 7, Tipo: TipoMovimiento.Salida, Motivo: MotivoMovimiento.Venta,
        Cantidad: cantidad, PrecioUnitario: 40m, Comentario: null);

    [Fact]
    public async Task Registrar_POSTMovimientos_ConForzarEnElBody_DevuelveElDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            movimientoId = 15, productoId = 7, tipo = 1, motivo = 1, cantidad = 8.0,
            precioUnitario = 40.0, stockAnterior = 12.0, stockNuevo = 4.0,
            fecha = "2026-07-10T14:00:00Z",
        }, HttpStatusCode.Created));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var registrado = await client.RegistrarAsync(Salida(), forzar: false);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"productoId\":7", fake.UltimoBody);
        Assert.Contains("\"tipo\":1", fake.UltimoBody);   // enum numérico
        Assert.Contains("\"forzar\":false", fake.UltimoBody);
        Assert.Equal(15, registrado.MovimientoId);
        Assert.Equal(4m, registrado.StockNuevo);
    }

    [Fact]
    public async Task Registrar_ConForzarTrue_LoEnviaEnElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            movimientoId = 16, productoId = 7, tipo = 1, motivo = 1, cantidad = 8.0,
            precioUnitario = 40.0, stockAnterior = 4.0, stockNuevo = -4.0,
            fecha = "2026-07-10T14:05:00Z",
        }, HttpStatusCode.Created));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        await client.RegistrarAsync(Salida(), forzar: true);

        Assert.Contains("\"forzar\":true", fake.UltimoBody);
    }

    [Fact]
    public async Task Registrar_409ConExtensiones_LanzaStockInsuficienteTipada()
    {
        // Mina 2: el VM hace catch (StockInsuficienteException ex) y usa ex.StockResultante
        // para preguntar "¿forzar?". Las extensiones del problem+json (Task 5) lo permiten.
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Regla de negocio violada.",
                detail = "Stock insuficiente para el producto 7.",
                status = 409,
                productoId = 7,
                stockActual = 5.0,
                cantidadSolicitada = 8.0,
            }),
        });
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(
            () => client.RegistrarAsync(Salida(), forzar: false));

        Assert.Equal(-3m, ex.StockResultante);
    }

    [Fact]
    public async Task Registrar_409SinExtensiones_LanzaReglaDeNegocioPlano()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "El producto está inactivo."));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.RegistrarAsync(Salida(), forzar: false));

        Assert.Equal("El producto está inactivo.", ex.Message);
    }

    [Fact]
    public async Task Historial_GETConTodosLosFiltros()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));
        var filtro = new HistorialMovimientoFiltro(
            ProductoId: 7,
            Tipo: TipoMovimiento.Salida,
            FechaDesde: new DateTime(2026, 7, 1),
            FechaHasta: new DateTime(2026, 7, 10));

        await client.ObtenerHistorialAsync(filtro);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/movimientos/historial?", pathAndQuery);
        Assert.Contains("productoId=7", pathAndQuery);
        Assert.Contains("tipo=1", pathAndQuery); // enum numérico en la query
        Assert.Contains("fechaDesde=2026-07-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("fechaHasta=2026-07-10T00%3A00%3A00.0000000", pathAndQuery);
    }

    [Fact]
    public async Task Historial_SinFiltros_GETSinQuery_MapeaLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                movimientoId = 1, productoId = 7, productoNombre = "Agua 2L", tipo = 0, motivo = 0,
                cantidad = 10.0, precioUnitario = 25.5, stockAnterior = 0.0, stockNuevo = 10.0,
                comentario = (string?)null, fecha = "2026-07-01T10:00:00Z",
                usuarioId = 1, usuarioNombre = "admin",
            },
        }));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var historial = await client.ObtenerHistorialAsync(new HistorialMovimientoFiltro());

        Assert.Equal("/movimientos/historial", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var item = Assert.Single(historial);
        Assert.Equal("Agua 2L", item.ProductoNombre);
        Assert.Equal(TipoMovimiento.Entrada, item.Tipo);
        Assert.Equal(10m, item.StockNuevo);
        Assert.Equal("admin", item.UsuarioNombre);
    }

    [Fact]
    public async Task RecalcularStock_POSTProductosIdRecalcularStock_DevuelveElResultado()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            productoId = 7, stockAnterior = 4.0, stockNuevo = 6.0, totalMovimientos = 12,
        }));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.RecalcularStockAsync(7);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/7/recalcular-stock", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(6m, resultado.StockNuevo);
        Assert.Equal(12, resultado.TotalMovimientos);
    }

    [Fact]
    public async Task Registrar_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Producto 99 no encontrado."));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.RegistrarAsync(Salida() with { ProductoId = 99 }, forzar: false));
    }
}
