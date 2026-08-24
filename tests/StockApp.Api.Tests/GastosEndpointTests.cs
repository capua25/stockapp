using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task PostGastos_FechaSinZonaHoraria_Devuelve201()
    {
        // Mismo bug que en /finanzas/ingresos (ver comentario ahí): JSON crudo con "fecha"
        // pelada (sin offset) deserializa a DateTime Kind=Unspecified, y Npgsql rechaza
        // escribirlo en la columna timestamptz -> 500 antes del fix.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var json = $$"""
            {"proveedorId":{{proveedorId}},"numeroFactura":"SINZONA-01","numeroOrden":null,
            "detalle":"Gasto sin zona horaria","destino":null,"fecha":"2026-01-15","montoTotal":1500,
            "fuenteFinanciamientoId":{{fuenteId}},"rubroGastoId":{{rubroId}},"lineaPoaId":null,
            "condicionPago":0,"fechaVencimiento":null,"movimientoIds":null}
            """;
        var response = await client.PostAsync("/finanzas/gastos",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_FechaVencimientoSinZonaHoraria_Devuelve201()
    {
        // Mismo bug, pero en el campo opcional FechaVencimiento (DateTime?) — condición
        // Crédito exige que venga completo.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var json = $$"""
            {"proveedorId":{{proveedorId}},"numeroFactura":"SINZONA-02","numeroOrden":null,
            "detalle":"Gasto a crédito sin zona horaria","destino":null,"fecha":"2026-01-15","montoTotal":1500,
            "fuenteFinanciamientoId":{{fuenteId}},"rubroGastoId":{{rubroId}},"lineaPoaId":null,
            "condicionPago":1,"fechaVencimiento":"2026-02-15","movimientoIds":null}
            """;
        var response = await client.PostAsync("/finanzas/gastos",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
    public async Task DeleteGasto_ConMovimientosYStockInsuficiente_Devuelve409()
    {
        // Matriz E2E de la decisión 9: un gasto vinculado a mano (AsociarMovimientosAsync, el
        // flujo "asociar factura" ya existente) también pasa por el asiento inverso al anularse,
        // y si el stock ya se consumió, el DELETE existente ahora devuelve 409 en vez de 200.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        int productoId;
        await using (var ctx = Factory.CrearContexto())
        {
            var producto = new Producto
            {
                Codigo = $"DEL-{Guid.NewGuid():N}", Nombre = "Producto con movimiento",
                UnidadMedida = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" },
                PrecioCosto = 10m, StockActual = 2m,
                Activo = true, FechaAlta = DateTime.UtcNow,
            };
            ctx.Add(producto);
            await ctx.SaveChangesAsync();
            productoId = producto.Id;

            ctx.MovimientosStock.Add(new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = 1, Tipo = TipoMovimiento.Entrada,
                Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m,
                Fecha = DateTime.UtcNow, GastoId = creado!.Id,
            });
            await ctx.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True((await verificacion.Gastos.SingleAsync(g => g.Id == creado.Id)).Activo);
        Assert.Equal(2m, (await verificacion.Productos.FindAsync(productoId))!.StockActual);
    }

    [Fact]
    public async Task DeleteGasto_ConMovimientosYStockSuficiente_RevierteElStockPorAsientoInverso()
    {
        // Ronda de correcciones 1, Hallazgo 2: el camino exitoso de la decisión 9 (anular por
        // DELETE un gasto CON movimientos y stock suficiente) solo estaba probado a nivel de
        // repositorio (Task 5). Este test cierra el agujero real por la puerta que usa el
        // usuario: el endpoint DELETE existente. Vincula el movimiento a mano, como hace
        // AsociarMovimientosAsync (el flujo "asociar factura" ya existente), no vía la pantalla
        // nueva de ingreso por factura.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        int productoId;
        await using (var ctx = Factory.CrearContexto())
        {
            var producto = new Producto
            {
                Codigo = $"DEL-OK-{Guid.NewGuid():N}", Nombre = "Producto con stock suficiente",
                UnidadMedida = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" },
                PrecioCosto = 10m, StockActual = 20m,
                Activo = true, FechaAlta = DateTime.UtcNow,
            };
            ctx.Add(producto);
            await ctx.SaveChangesAsync();
            productoId = producto.Id;

            ctx.MovimientosStock.Add(new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = 1, Tipo = TipoMovimiento.Entrada,
                Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m,
                Fecha = DateTime.UtcNow, GastoId = creado!.Id,
            });
            await ctx.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.False((await verificacion.Gastos.SingleAsync(g => g.Id == creado.Id)).Activo);
        Assert.Equal(15m, (await verificacion.Productos.FindAsync(productoId))!.StockActual);   // 20 - 5

        var salida = await verificacion.MovimientosStock.SingleAsync(m =>
            m.ProductoId == productoId && m.Tipo == TipoMovimiento.Salida);
        Assert.Equal(MotivoMovimiento.Ajuste, salida.Motivo);
        Assert.Equal(5m, salida.Cantidad);
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
    public async Task DeleteGasto_ContadoConPagoAutomaticoSinConfirmar_Devuelve409ConDatosEstructurados()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, factura: "CASCADA-01")))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado!.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(creado.Id, body.GetProperty("gastoId").GetInt32());
        Assert.Equal(1500m, body.GetProperty("montoPagoAutomatico").GetDecimal());

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.Include(g => g.Pagos).SingleAsync(g => g.Id == creado.Id);
        Assert.True(gasto.Activo);
        Assert.True(Assert.Single(gasto.Pagos).Activo);
    }

    [Fact]
    public async Task DeleteGasto_ContadoConPagoAutomaticoConfirmado_AnulaGastoYElPago()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, factura: "CASCADA-02")))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado!.Id}?confirmar=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.Include(g => g.Pagos).SingleAsync(g => g.Id == creado.Id);
        Assert.False(gasto.Activo);
        Assert.False(Assert.Single(gasto.Pagos).Activo);
    }

    [Fact]
    public async Task GetGasto_ExponeTieneMovimientosDeStockSegunCorresponda()
    {
        // Deuda A parte 2: el ViewModel necesita, ANTES de anular, saber si el gasto tiene
        // movimientos de stock asociados (para advertir el descuento de stock en el dialogo).
        // Test end-to-end contra Postgres real: no un mock que se completaria a si mismo, sino
        // el shape real de la respuesta HTTP tras un INSERT real en MovimientosStock.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var conMovimiento = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito, factura: "STOCK-DTO-01")))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        var sinMovimiento = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito, factura: "STOCK-DTO-02")))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        await using (var ctx = Factory.CrearContexto())
        {
            var producto = new Producto
            {
                Codigo = $"STK-DTO-{Guid.NewGuid():N}", Nombre = "Producto con movimiento",
                UnidadMedida = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" },
                PrecioCosto = 10m, StockActual = 20m,
                Activo = true, FechaAlta = DateTime.UtcNow,
            };
            ctx.Add(producto);
            await ctx.SaveChangesAsync();

            ctx.MovimientosStock.Add(new MovimientoStock
            {
                ProductoId = producto.Id, UsuarioId = 1, Tipo = TipoMovimiento.Entrada,
                Motivo = MotivoMovimiento.Compra, Cantidad = 5m, PrecioUnitario = 10m,
                Fecha = DateTime.UtcNow, GastoId = conMovimiento!.Id,
            });
            await ctx.SaveChangesAsync();
        }

        var dtoConMovimiento = await (await client.GetAsync($"/finanzas/gastos/{conMovimiento!.Id}"))
            .Content.ReadFromJsonAsync<GastoDto>();
        var dtoSinMovimiento = await (await client.GetAsync($"/finanzas/gastos/{sinMovimiento!.Id}"))
            .Content.ReadFromJsonAsync<GastoDto>();

        Assert.True(dtoConMovimiento!.TieneMovimientosDeStock);
        Assert.False(dtoSinMovimiento!.TieneMovimientosDeStock);
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
