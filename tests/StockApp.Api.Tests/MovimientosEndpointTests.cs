using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class MovimientosEndpointTests : ApiTestBase
{
    public MovimientosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── POST /movimientos ────────────────────────────────────────────────────

    [Fact]
    public async Task PostMovimientos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(1, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostMovimientos_ConTokenOperador_RegistraEntradaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        // El movimiento registra auditoría con FK real a Usuarios; TokenOperador() reclama
        // UsuarioId=2, así que sembramos Admin (Id=1) + Operador (Id=2) en ese orden.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M1", "Producto Mov 1", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, "Compra test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registrado = await response.Content.ReadFromJsonAsync<MovimientoRegistradoDto>();
        Assert.Equal(15m, registrado!.StockNuevo);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(15m, actualizado.StockActual);
    }

    [Fact]
    public async Task PostMovimientos_SalidaMayorAlStock_SinForzar_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M2", "Producto Mov 2", 3m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Salida, MotivoMovimiento.Venta, 10m, 20m, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostMovimientos_SalidaMayorAlStock_ConForzar_Devuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        // Idem: con Forzar=true el movimiento se registra igual, requiere Usuario real para la FK de auditoría.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M3", "Producto Mov 3", 3m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Salida, MotivoMovimiento.Venta, 10m, 20m, null, Forzar: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── GET /movimientos/historial ───────────────────────────────────────────

    [Fact]
    public async Task GetHistorial_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/movimientos/historial");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ConTokenOperador_FiltraPorProductoId()
    {
        await using var ctx = Factory.CrearContexto();
        // El POST previo a /movimientos escribe auditoría con FK real a Usuarios.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M4", "Producto Mov 4", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        var response = await client.GetAsync($"/movimientos/historial?productoId={producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var historial = await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>();
        Assert.Single(historial!);
        Assert.Equal(producto.Id, historial![0].ProductoId);
    }

    [Fact]
    public async Task GetHistorial_FiltraPorTipo_Entrada()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M6", "Producto Mov 6", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        // Registrar una Entrada
        await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        // Registrar una Salida
        await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Salida, MotivoMovimiento.Venta, 2m, 15m, null));

        var response = await client.GetAsync($"/movimientos/historial?tipo=Entrada");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var historial = await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>();
        Assert.Single(historial!);
        Assert.Equal(TipoMovimiento.Entrada, historial![0].Tipo);
    }

    [Fact]
    public async Task GetHistorial_FiltraPorRangoFechas()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M7", "Producto Mov 7", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        // Registrar un movimiento hoy
        await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        var ahora = DateTime.UtcNow;
        var ayer = ahora.AddDays(-1).ToString("O");
        var manana = ahora.AddDays(1).ToString("O");

        // Filtrar con rango que incluye hoy
        var responseConRango = await client.GetAsync($"/movimientos/historial?fechaDesde={Uri.EscapeDataString(ayer)}&fechaHasta={Uri.EscapeDataString(manana)}");

        Assert.Equal(HttpStatusCode.OK, responseConRango.StatusCode);
        var historialConRango = await responseConRango.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>();
        Assert.Single(historialConRango!);

        // Filtrar con rango en el pasado lejano (sin movimientos)
        var pasadoLejano = "2020-01-01T00:00:00Z";
        var responsePasado = await client.GetAsync($"/movimientos/historial?fechaHasta={Uri.EscapeDataString(pasadoLejano)}");

        Assert.Equal(HttpStatusCode.OK, responsePasado.StatusCode);
        var historialPasado = await responsePasado.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>();
        Assert.Empty(historialPasado!);
    }

    [Fact]
    public async Task GetHistorial_TipoInvalido_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        // Un tipo que no matchea el enum TipoMovimiento hace fallar el binding de Minimal API
        // (BadHttpRequestException), que DomainExceptionHandler mapea a 400: input inválido del
        // cliente, no un error del servidor.
        var response = await client.GetAsync("/movimientos/historial?tipo=Inexistente");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── POST /productos/{id}/recalcular-stock ────────────────────────────────

    [Fact]
    public async Task PostRecalcularStock_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/productos/1/recalcular-stock", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRecalcularStock_ConTokenOperador_RecalculaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        // RecalcularStockAsync también escribe auditoría con FK real a Usuarios.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M5", "Producto Mov 5", 999m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsync($"/productos/{producto.Id}/recalcular-stock", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<RecalculoResultadoDto>();
        Assert.Equal(0m, resultado!.StockNuevo); // sin movimientos previos: neto = 0

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(0m, actualizado.StockActual);
    }

    [Fact]
    public async Task PostRecalcularStock_ProductoInexistente_Devuelve404()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/productos/99999/recalcular-stock", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
