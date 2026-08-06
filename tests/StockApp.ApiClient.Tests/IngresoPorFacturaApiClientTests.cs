using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class IngresoPorFacturaApiClientTests
{
    private static IngresoPorFacturaDto DtoValido() => new(
        ProveedorId: 3, NumeroFactura: "A-0099", NumeroOrden: null,
        Fecha: new DateTime(2026, 7, 20), Detalle: "Compra de insumos", Destino: null,
        MontoTotal: 500m, FuenteFinanciamientoId: 4, RubroGastoId: 5, LineaPoaId: null,
        CondicionPago: CondicionPago.Contado, FechaVencimiento: null,
        Renglones: new[]
        {
            new RenglonFacturaDto(10, null, 5m, 90m, false),
            new RenglonFacturaDto(null, new ProductoNuevoDto("SKU-N", "Nuevo", null, 1, 50m), 2m, 25m, false),
        });

    [Fact]
    public async Task Registrar_POSTIngresoFactura_SerializaCabeceraYRenglones()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            gastoId = 42, movimientoIds = new[] { 1, 2 }, sumaRenglones = 500.0, diferenciaConTotal = 0.0,
        }, HttpStatusCode.Created));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.RegistrarAsync(DtoValido());

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos/ingreso-factura", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"proveedorId\":3", fake.UltimoBody);
        Assert.Contains("\"productoId\":10", fake.UltimoBody);
        Assert.Contains("\"codigo\":\"SKU-N\"", fake.UltimoBody);
        Assert.Equal(42, resultado.GastoId);
        Assert.Equal(2, resultado.MovimientoIds.Count);
    }

    [Fact]
    public async Task Registrar_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "La factura debe tener al menos un renglón."));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.RegistrarAsync(DtoValido()));
        Assert.Equal("La factura debe tener al menos un renglón.", ex.Message);
    }

    [Fact]
    public async Task Registrar_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe la factura 'A-0099' para ese proveedor."));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.RegistrarAsync(DtoValido()));
    }

    [Fact]
    public async Task AnularLoteAsync_POSTAnular_ConElGastoIdEnLaRuta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        await client.AnularLoteAsync(42);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos/ingreso-factura/42/anular", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("confirmar=true", fake.UltimaRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task AnularLoteAsync_ConConfirmar_EnviaElQueryParam()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        await client.AnularLoteAsync(42, confirmarAnulacionDePagoAutomatico: true);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos/ingreso-factura/42/anular", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("confirmar=true", fake.UltimaRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task AnularLoteAsync_409SinStock_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede anular: stock insuficiente en 1 producto(s)."));
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.AnularLoteAsync(42));
        Assert.Contains("stock insuficiente", ex.Message);
    }

    [Fact]
    public async Task AnularLoteAsync_409ConExtensiones_LanzaExcepcionEstructurada()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                title = "Regla de negocio violada.",
                detail = "El gasto 42 tiene un pago automático de contado activo por 500: " +
                         "anularlo también va a eliminar ese pago. Confirmá la anulación para continuar.",
                status = 409,
                gastoId = 42,
                montoPagoAutomatico = 500.0,
            }),
        });
        var client = new IngresoPorFacturaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<AnulacionRequierePagoAutomaticoConfirmadoException>(
            () => client.AnularLoteAsync(42));

        Assert.Equal(42, ex.GastoId);
        Assert.Equal(500m, ex.MontoPagoAutomatico);
    }
}
