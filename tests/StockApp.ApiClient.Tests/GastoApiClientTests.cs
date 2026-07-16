using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class GastoApiClientTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static object GastoJson(int id = 1, string estado = "Pendiente", bool activo = true) => new
    {
        id,
        proveedorId = 3,
        proveedorNombre = "Barraca X",
        numeroFactura = "A-0001",
        numeroOrden = (string?)null,
        detalle = "Materiales",
        destino = (string?)null,
        fecha = Hoy,
        montoTotal = 1000m,
        fuenteFinanciamientoId = 2,
        fuenteNombre = "Literal B",
        rubroGastoId = 4,
        rubroNombre = "Materiales",
        lineaPoaId = (int?)null,
        lineaPoaNombre = (string?)null,
        condicionPago = 1,             // Credito
        fechaVencimiento = Hoy.AddDays(30),
        activo,
        totalPagado = 0m,
        estado,
        pagos = new[]
        {
            new { id = 9, fecha = Hoy, monto = 0m, nota = (string?)null, activo = true },
        },
    };

    private static Gasto GastoEntidad() => new()
    {
        ProveedorId = 3,
        NumeroFactura = "A-0001",
        Detalle = "Materiales",
        Fecha = Hoy,
        MontoTotal = 1000m,
        FuenteFinanciamientoId = 2,
        RubroGastoId = 4,
        CondicionPago = CondicionPago.Credito,
        FechaVencimiento = Hoy.AddDays(30),
    };

    [Fact]
    public async Task Listar_GETConFiltros_MapeaEntidadesConNavsYPagos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[] { GastoJson() }));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var gastos = await client.ListarAsync(new GastoFiltro(
            FechaDesde: Hoy.AddDays(-30), ProveedorId: 3));

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("fechaDesde=", fake.UltimaRequest.RequestUri.Query);
        Assert.Contains("proveedorId=3", fake.UltimaRequest.RequestUri.Query);

        var gasto = Assert.Single(gastos);
        Assert.Equal("Barraca X", gasto.Proveedor!.Nombre);
        Assert.Equal("Literal B", gasto.FuenteFinanciamiento!.Nombre);
        Assert.Equal(CondicionPago.Credito, gasto.CondicionPago);
        Assert.Single(gasto.Pagos);
    }

    [Fact]
    public async Task Listar_SinFiltros_NoAgregaQuery()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.ListarAsync(new GastoFiltro());

        Assert.Equal(string.Empty, fake.UltimaRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Alta_POSTConMovimientos_DevuelveIdYAdvertencia()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new { id = 7, advertenciaSobregiro = "Atención: sobregiro" }, HttpStatusCode.Created));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AltaAsync(GastoEntidad(), new[] { 40, 41 });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"movimientoIds\":[40,41]", fake.UltimoBody);
        Assert.Equal(7, resultado.Id);
        Assert.Equal("Atención: sobregiro", resultado.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new { id = 5, advertenciaSobregiro = (string?)null }));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));
        var gasto = GastoEntidad();
        gasto.Id = 5;

        var resultado = await client.ModificarAsync(gasto);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos/5", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Null(resultado.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task Anular_DELETEGastos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.AnularAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RegistrarPago_POSTPagos_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 21 }, HttpStatusCode.Created));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var pagoId = await client.RegistrarPagoAsync(new PagoGasto
        {
            GastoId = 5, Fecha = Hoy, Monto = 300m, Nota = "parcial",
        });

        Assert.Equal("/finanzas/gastos/5/pagos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"monto\":300", fake.UltimoBody);
        Assert.Equal(21, pagoId);
    }

    [Fact]
    public async Task AnularPago_DELETEPagos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.AnularPagoAsync(5, 21);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos/5/pagos/21", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AsociarMovimientos_POSTMovimientos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.AsociarMovimientosAsync(5, new[] { 40 });

        Assert.Equal("/finanzas/gastos/5/movimientos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"movimientoIds\":[40]", fake.UltimoBody);
    }

    [Fact]
    public async Task ObtenerPorProveedorYFactura_404_DevuelveNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "No existe."));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var gasto = await client.ObtenerPorProveedorYFacturaAsync(3, "NO-EXISTE");

        Assert.Null(gasto);
        Assert.Contains("/finanzas/gastos/por-factura", fake.UltimaRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe la factura 'A-0001' para ese proveedor."));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(GastoEntidad()));

        Assert.Equal("Ya existe la factura 'A-0001' para ese proveedor.", ex.Message);
    }
}
