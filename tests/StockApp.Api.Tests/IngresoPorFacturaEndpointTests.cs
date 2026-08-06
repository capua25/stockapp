using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class IngresoPorFacturaEndpointTests : ApiTestBase
{
    public IngresoPorFacturaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Seed de maestros + un producto activo con stock, para probar el camino feliz.</summary>
    private async Task<(int proveedorId, int fuenteId, int rubroId, int productoId)> SeedMaestrosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro ingreso" };
        ctx.AddRange(proveedor, fuente, rubro);
        await ctx.SaveChangesAsync();

        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "IPF-01", "Producto IPF", 5m);

        return (proveedor.Id, fuente.Id, rubro.Id, producto.Id);
    }

    private static IngresoPorFacturaRequest RequestValido(
        int proveedorId, int fuenteId, int rubroId, int productoId, string? factura = null,
        CondicionPago condicion = CondicionPago.Contado, DateTime? fechaVencimiento = null) => new(
        ProveedorId: proveedorId, NumeroFactura: factura, NumeroOrden: null,
        Fecha: DateTime.UtcNow, Detalle: "Compra vía API", Destino: null, MontoTotal: 100m,
        FuenteFinanciamientoId: fuenteId, RubroGastoId: rubroId, LineaPoaId: null,
        CondicionPago: condicion, FechaVencimiento: fechaVencimiento,
        Lineas: new List<RenglonFacturaRequest>
        {
            new(productoId, null, 5m, 20m, false),
        });

    [Fact]
    public async Task PostIngresoFactura_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PostAsJsonAsync("/movimientos/ingreso-factura", RequestValido(1, 1, 1, 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIngresoFactura_ConTokenOperador_Crea201ConElResultado()
    {
        // Spec decisión 5: RegistrarMovimientos + RegistrarGastos + GestionarProductos los tiene
        // Admin Y Operador — no hay 403 por rol posible en este endpoint (AuthorizationService.cs).
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-API-01"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>();
        Assert.True(resultado!.GastoId > 0);
        Assert.Single(resultado.MovimientoIds);
        Assert.Equal(100m, resultado.SumaRenglones);   // 5 * 20
        Assert.Equal(0m, resultado.DiferenciaConTotal); // 100 - 100

        await using var verificacion = Factory.CrearContexto();
        var producto = await verificacion.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal(10m, producto.StockActual);   // 5 + 5
    }

    [Fact]
    public async Task PostIngresoFactura_RenglonesVacios_Devuelve400()
    {
        var (proveedorId, fuenteId, rubroId, _) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var request = RequestValido(proveedorId, fuenteId, rubroId, 1) with { Lineas = new List<RenglonFacturaRequest>() };

        var response = await client.PostAsJsonAsync("/movimientos/ingreso-factura", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostIngresoFactura_FacturaDuplicada_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var primera = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-DUP-01"));
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var segunda = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-DUP-01"));

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task PostAnular_SinStockSuficiente_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        // Crédito a propósito: no crea pago automático de contado, así el 409 que se prueba
        // acá es genuinamente el de stock insuficiente, no el nuevo guard de "falta confirmar
        // la anulación del pago automático" (que dispara ANTES y taparía este camino si la
        // factura fuera de contado).
        var creado = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-ANU-01",
                condicion: CondicionPago.Credito, fechaVencimiento: DateTime.UtcNow.AddDays(30)));
        var resultado = await creado.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>();

        // Consumir el stock recién ingresado hasta dejarlo por debajo de lo necesario para revertir.
        await using (var ctx = Factory.CrearContexto())
        {
            await ctx.Productos.Where(p => p.Id == productoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, 1m));
        }

        var response = await client.PostAsync($"/movimientos/ingreso-factura/{resultado!.GastoId}/anular", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAnular_ContadoConPagoAutomaticoSinConfirmar_Devuelve409ConDatosEstructurados()
    {
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-CASCADA-01"));
        var resultado = await creado.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>();

        var response = await client.PostAsync(
            $"/movimientos/ingreso-factura/{resultado!.GastoId}/anular", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(resultado.GastoId, body.GetProperty("gastoId").GetInt32());
        Assert.Equal(100m, body.GetProperty("montoPagoAutomatico").GetDecimal());

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.Include(g => g.Pagos).SingleAsync(g => g.Id == resultado.GastoId);
        Assert.True(gasto.Activo);
        Assert.True(Assert.Single(gasto.Pagos).Activo);
    }

    [Fact]
    public async Task PostAnular_ContadoConPagoAutomaticoConfirmado_AnulaYRevierteElStock()
    {
        var (proveedorId, fuenteId, rubroId, productoId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await client.PostAsJsonAsync("/movimientos/ingreso-factura",
            RequestValido(proveedorId, fuenteId, rubroId, productoId, factura: "IPF-CASCADA-02"));
        var resultado = await creado.Content.ReadFromJsonAsync<IngresoPorFacturaResultadoDto>();

        var response = await client.PostAsync(
            $"/movimientos/ingreso-factura/{resultado!.GastoId}/anular?confirmar=true", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.Include(g => g.Pagos).SingleAsync(g => g.Id == resultado.GastoId);
        Assert.False(gasto.Activo);
        Assert.False(Assert.Single(gasto.Pagos).Activo);
        var producto = await verificacion.Productos.SingleAsync(p => p.Id == productoId);
        Assert.Equal(5m, producto.StockActual);   // 5 (seed) + 5 (ingreso) - 5 (anulación) = 5, una sola vez
    }
}
