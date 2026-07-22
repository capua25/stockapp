using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class GastosEndpointTests : ApiTestBase
{
    public GastosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Seed de los maestros que el gasto exige por FK + los DOS usuarios auditores:
    /// la auditoría escribe con el usuarioId del token (1 = Admin, 2 = Operador) y su
    /// FK Restrict a Usuarios exige que ambos existan.
    /// </summary>
    private async Task<(int proveedorId, int fuenteId, int rubroId)> SeedMaestrosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        if (!await ctx.Usuarios.AnyAsync())
        {
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
            await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        }

        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro api" };
        ctx.AddRange(proveedor, fuente, rubro);
        await ctx.SaveChangesAsync();
        return (proveedor.Id, fuente.Id, rubro.Id);
    }

    private static CrearGastoRequest RequestValido(
        int proveedorId, int fuenteId, int rubroId,
        CondicionPago condicion = CondicionPago.Contado, string? factura = null) => new(
        ProveedorId: proveedorId,
        NumeroFactura: factura,
        NumeroOrden: null,
        Detalle: "Gasto vía API",
        Destino: null,
        Fecha: DateTime.UtcNow,
        MontoTotal: 1500m,
        FuenteFinanciamientoId: fuenteId,
        RubroGastoId: rubroId,
        LineaPoaId: null,
        CondicionPago: condicion,
        FechaVencimiento: condicion == CondicionPago.Credito ? DateTime.UtcNow.AddDays(30) : null,
        MovimientoIds: null);

    [Fact]
    public async Task GetGastos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/gastos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PostAsJsonAsync("/finanzas/gastos", RequestValido(1, 1, 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_Contado_Crea201ConPagoAutomatico()
    {
        // Spec Finanzas §9: RegistrarGastos lo tienen Admin Y Operador — no hay 403 por rol.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId, factura: "API-0001"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = await response.Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        Assert.NotNull(creado);
        Assert.Null(creado!.AdvertenciaSobregiro);

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.Include(g => g.Pagos)
            .SingleAsync(g => g.Id == creado.Id);
        Assert.Equal("API-0001", gasto.NumeroFactura);
        var pago = Assert.Single(gasto.Pagos);           // pago contado automático
        Assert.Equal(1500m, pago.Monto);
    }

    [Fact]
    public async Task PostGastos_CreditoSinVencimiento_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)
            with { FechaVencimiento = null };

        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_MontoNoPositivo_Devuelve400()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId) with { MontoTotal = 0m };

        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_FacturaDuplicada_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId, factura: "DUP-01");

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/finanzas/gastos", request)).StatusCode);
        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_SobregiroLineaPoa_Crea201ConAdvertencia()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        await using (var ctx = Factory.CrearContexto())
        {
            ctx.Add(new LineaPoa
            {
                Nombre = $"PRENSA {Guid.NewGuid():N}", Programa = "Com", Ejercicio = 2026,
                Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteId, Monto = 1000m } },
            });
            await ctx.SaveChangesAsync();
        }
        int lineaId;
        await using (var ctx = Factory.CrearContexto())
            lineaId = await ctx.LineasPoa.OrderByDescending(l => l.Id).Select(l => l.Id).FirstAsync();

        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId) with { LineaPoaId = lineaId };  // 1500 > 1000

        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);  // advierte pero NO bloquea
        var creado = await response.Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        Assert.NotNull(creado!.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task GetGastos_FiltraPorProveedor_YDevuelveEstadoCalculado()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId));  // contado ⇒ Pagada

        var response = await client.GetAsync($"/finanzas/gastos?proveedorId={proveedorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gastos = await response.Content.ReadFromJsonAsync<List<GastoDto>>();
        var gasto = Assert.Single(gastos!);
        Assert.Equal("Pagada", gasto.Estado);
        Assert.Equal(1500m, gasto.TotalPagado);
        Assert.NotNull(gasto.ProveedorNombre);
    }

    [Fact]
    public async Task GetGastoPorId_Inexistente_Devuelve404()
    {
        await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/gastos/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetGastoPorFactura_ExistenteEInexistente()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId, factura: "BUSCA-01"));

        var ok = await client.GetAsync(
            $"/finanzas/gastos/por-factura?proveedorId={proveedorId}&numeroFactura=BUSCA-01");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = await ok.Content.ReadFromJsonAsync<GastoDto>();
        Assert.Equal("BUSCA-01", dto!.NumeroFactura);

        var notFound = await client.GetAsync(
            $"/finanzas/gastos/por-factura?proveedorId={proveedorId}&numeroFactura=NO-EXISTE");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task GetGastoPorFactura_FiltraPorNumeroOrden()
    {
        // F5c: dos gastos activos del mismo proveedor pueden compartir número de factura con
        // distinto NumeroOrden (índice ampliado) — /por-factura tiene que devolver el que
        // matchea el orden pedido, no cualquiera de los dos.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId, factura: "BUSCA-02") with { NumeroOrden = "OC-1" });
        await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId, factura: "BUSCA-02") with { NumeroOrden = "OC-2" });

        var ok = await client.GetAsync(
            $"/finanzas/gastos/por-factura?proveedorId={proveedorId}&numeroFactura=BUSCA-02&numeroOrden=OC-1");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = await ok.Content.ReadFromJsonAsync<GastoDto>();
        Assert.Equal("OC-1", dto!.NumeroOrden);

        var notFound = await client.GetAsync(
            $"/finanzas/gastos/por-factura?proveedorId={proveedorId}&numeroFactura=BUSCA-02&numeroOrden=OC-9");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task PostPagos_RegistraYRespetaSaldo()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var pago = await client.PostAsJsonAsync($"/finanzas/gastos/{creado!.Id}/pagos",
            new RegistrarPagoRequest(DateTime.UtcNow, 1000m, "primer pago"));
        Assert.Equal(HttpStatusCode.Created, pago.StatusCode);

        // El saldo quedó en 500: pagar 600 debe dar 409 (no pagar más que el saldo)
        var excedido = await client.PostAsJsonAsync($"/finanzas/gastos/{creado.Id}/pagos",
            new RegistrarPagoRequest(DateTime.UtcNow, 600m, null));
        Assert.Equal(HttpStatusCode.Conflict, excedido.StatusCode);
    }

    [Fact]
    public async Task DeletePago_AnulaYElGastoVuelveAPendiente()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        var pago = await (await client.PostAsJsonAsync($"/finanzas/gastos/{creado!.Id}/pagos",
                new RegistrarPagoRequest(DateTime.UtcNow, 1500m, null)))
            .Content.ReadFromJsonAsync<PagoCreadoResponse>();

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado.Id}/pagos/{pago!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await (await client.GetAsync($"/finanzas/gastos/{creado.Id}"))
            .Content.ReadFromJsonAsync<GastoDto>();
        Assert.Equal("Pendiente", dto!.Estado);
        Assert.Equal(0m, dto.TotalPagado);
    }

    [Fact]
    public async Task DeleteGasto_ConPagosActivos409_SinPagosAnula()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var contado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId)))   // contado ⇒ pago activo
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var conPagos = await client.DeleteAsync($"/finanzas/gastos/{contado!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, conPagos.StatusCode);

        var credito = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        var sinPagos = await client.DeleteAsync($"/finanzas/gastos/{credito!.Id}");
        Assert.Equal(HttpStatusCode.OK, sinPagos.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.False((await verificacion.Gastos.SingleAsync(g => g.Id == credito.Id)).Activo);
    }

    [Fact]
    public async Task PutGasto_Modifica200ConCambios()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var response = await client.PutAsJsonAsync($"/finanzas/gastos/{creado!.Id}",
            new ModificarGastoRequest(
                ProveedorId: proveedorId, NumeroFactura: null, NumeroOrden: "OC-77",
                Detalle: "Gasto vía API (editado)", Destino: "Corralón",
                Fecha: DateTime.UtcNow, MontoTotal: 1800m,
                FuenteFinanciamientoId: fuenteId, RubroGastoId: rubroId, LineaPoaId: null,
                CondicionPago: CondicionPago.Credito, FechaVencimiento: DateTime.UtcNow.AddDays(60)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.SingleAsync(g => g.Id == creado.Id);
        Assert.Equal("Gasto vía API (editado)", gasto.Detalle);
        Assert.Equal(1800m, gasto.MontoTotal);
        Assert.Equal("OC-77", gasto.NumeroOrden);
    }

    [Fact]
    public async Task PostMovimientos_AsociaEntradasAlGasto()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();

        int movimientoId;
        await using (var ctx = Factory.CrearContexto())
        {
            var unidad = new UnidadMedida
            {
                Nombre = $"Unidad {Guid.NewGuid():N}", Abreviatura = Guid.NewGuid().ToString("N")[..8],
            };
            var usuario = await ctx.Usuarios.FirstAsync();
            ctx.Add(unidad);
            await ctx.SaveChangesAsync();
            var producto = new Producto
            {
                Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Prod api", UnidadMedidaId = unidad.Id,
            };
            ctx.Add(producto);
            await ctx.SaveChangesAsync();
            var movimiento = new MovimientoStock
            {
                ProductoId = producto.Id, UsuarioId = usuario.Id,
                Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra,
                Cantidad = 3m, PrecioUnitario = 500m, Fecha = DateTime.UtcNow,
            };
            ctx.Add(movimiento);
            await ctx.SaveChangesAsync();
            movimientoId = movimiento.Id;
        }

        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var response = await client.PostAsJsonAsync($"/finanzas/gastos/{creado!.Id}/movimientos",
            new AsociarMovimientosRequest(new List<int> { movimientoId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        var vinculado = await verificacion.MovimientosStock.SingleAsync(m => m.Id == movimientoId);
        Assert.Equal(creado.Id, vinculado.GastoId);
    }
}
