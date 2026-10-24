using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class FinanzasVistasEndpointTests : ApiTestBase
{
    public FinanzasVistasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedUsuarioAdminAsync()
    {
        await using var ctx = Factory.CrearContexto();
        if (!await ctx.Usuarios.AnyAsync())
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetControlPoa_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/control-poa?ejercicio=2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCalendarioPagos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/calendario-pagos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 200 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_ConMes_Devuelve200ConLibroCajaMesDto()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaMesDto>();
        Assert.NotNull(dto);
        Assert.Equal(2026, dto!.Anio);
        Assert.Equal(7, dto.Mes);
    }

    [Fact]
    public async Task GetLibroCaja_SinMes_Devuelve200ConLibroCajaAnualDto()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaAnualDto>();
        Assert.NotNull(dto);
        Assert.Equal(12, dto!.TotalesPorMes.Count);
    }

    [Fact]
    public async Task GetControlPoa_Devuelve200ConListaVacia_SinLineasDelEjercicio()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/control-poa?ejercicio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<List<ControlPoaLineaDto>>();
        Assert.NotNull(dto);
        Assert.Empty(dto!);
    }

    [Fact]
    public async Task GetCalendarioPagos_Devuelve200ConCalendarioVacio()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/calendario-pagos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CalendarioPagosDto>();
        Assert.NotNull(dto);
        Assert.Empty(dto!.Vencidas);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_MesFueraDeRango_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLibroCaja_SinAnio_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetControlPoa_SinEjercicio_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/control-poa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Caja ─────────────────────────────────────────────────────────────────

    // Montos con un único origen posible: Local = 1000 de ingreso y 5000 de pago; POA = 20 de ingreso y 3 de pago.
    // Saldo inicial de julio: POA -500 y Local -60000 (pagos de junio). Los inactivos (ingreso POA 400, pago POA 70, gasto anulado con pagos 9000 POA y 80000 Local) no cuentan en ninguna caja.
    private async Task SeedLibroDeDosCajasAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = new FuenteFinanciamiento { Nombre = "Literal local", Activo = true, Caja = Caja.Local };
        var fuentePoa = new FuenteFinanciamiento { Nombre = "Literal POA", Activo = true, Caja = Caja.Poa };
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var rubro = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro caja" };
        ctx.AddRange(fuente, fuentePoa, proveedor, rubro);
        await ctx.SaveChangesAsync();

        var fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        ctx.IngresosCaja.AddRange(
            new IngresoCaja { Fecha = fecha, Concepto = "Ingreso local", FuenteFinanciamientoId = fuente.Id, Monto = 1000m, Caja = Caja.Local },
            new IngresoCaja { Fecha = fecha, Concepto = "Ingreso POA", FuenteFinanciamientoId = fuentePoa.Id, Monto = 20m, Caja = Caja.Poa },
            new IngresoCaja { Fecha = fecha, Concepto = "Ingreso POA anulado", FuenteFinanciamientoId = fuentePoa.Id, Monto = 400m, Caja = Caja.Poa, Activo = false });

        Gasto NuevoGasto(string detalle, bool activo) => new()
        {
            ProveedorId = proveedor.Id, RubroGastoId = rubro.Id, Detalle = detalle, Fecha = fecha,
            MontoTotal = 100000m, CondicionPago = CondicionPago.Credito, FechaVencimiento = fecha.AddDays(30), Activo = activo,
        };
        var activo = NuevoGasto("Gasto activo", true);
        activo.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 5000m, Caja = Caja.Local, FuenteFinanciamientoId = fuente.Id });
        activo.Pagos.Add(PagoGasto.Automatico(fecha, 3m, "automático POA", Caja.Poa, fuentePoa.Id));
        activo.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 70m, Caja = Caja.Poa, Activo = false, FuenteFinanciamientoId = fuentePoa.Id });
        // Pagos de junio: arman el saldo inicial de julio de cada caja (POA -500, Local -60000).
        var junio = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        activo.Pagos.Add(new PagoGasto { Fecha = junio, Monto = 500m, Caja = Caja.Poa, FuenteFinanciamientoId = fuentePoa.Id });
        activo.Pagos.Add(new PagoGasto { Fecha = junio, Monto = 60000m, Caja = Caja.Local, FuenteFinanciamientoId = fuente.Id });
        var anulado = NuevoGasto("Gasto anulado", false);
        anulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 9000m, Caja = Caja.Poa, FuenteFinanciamientoId = fuentePoa.Id });
        anulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 80000m, Caja = Caja.Local, FuenteFinanciamientoId = fuente.Id });
        ctx.Gastos.AddRange(activo, anulado);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLibroCaja_CajaPoa_NoIncluyeMovimientosLocales()
    {
        await SeedLibroDeDosCajasAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=7&caja=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaMesDto>();
        Assert.Equal(new[] { "Ingreso POA", "Gasto activo" }, dto!.Movimientos.Select(m => m.Concepto));
        Assert.Equal(new[] { 20m, 0m }, dto.Movimientos.Select(m => m.Ingreso));
        Assert.Equal(new[] { 0m, 3m }, dto.Movimientos.Select(m => m.Egreso));
        Assert.Equal(-500m, dto.SaldoInicial);
        Assert.Equal(-483m, dto.SaldoFinal);
    }

    [Fact]
    public async Task GetLibroCaja_SinCaja_EsLocal()
    {
        await SeedLibroDeDosCajasAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        var dto = await response.Content.ReadFromJsonAsync<LibroCajaMesDto>();
        Assert.Equal(new[] { 1000m, 0m }, dto!.Movimientos.Select(m => m.Ingreso));
        Assert.Equal(new[] { 0m, 5000m }, dto.Movimientos.Select(m => m.Egreso));
        Assert.Equal(-60000m, dto.SaldoInicial);
        Assert.Equal(-64000m, dto.SaldoFinal);
    }

    [Fact]
    public async Task GetLibroCaja_AnualCajaPoa_SoloPoa()
    {
        await SeedLibroDeDosCajasAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&caja=1");

        var dto = await response.Content.ReadFromJsonAsync<LibroCajaAnualDto>();
        var julio = dto!.TotalesPorMes.Single(m => m.Mes == 7);
        Assert.Equal(20m, julio.Ingresos);
        Assert.Equal(3m, julio.Egresos);
    }

    [Theory]
    [InlineData("anio=2026&mes=7&caja=7")]
    [InlineData("anio=2026&caja=7")]
    [InlineData("anio=2026&mes=7&caja=-1")]
    [InlineData("anio=2026&mes=7&caja=abc")]
    public async Task GetLibroCaja_Caja7_Devuelve400(string query)
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
