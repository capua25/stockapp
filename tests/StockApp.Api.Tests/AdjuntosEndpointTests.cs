using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class AdjuntosEndpointTests : ApiTestBase
{
    public AdjuntosEndpointTests(ApiFactory factory) : base(factory) { }

    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

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
    /// Siembra un gasto + los DOS usuarios auditores (1 = Admin, 2 = Operador) que la
    /// auditoría de Adjunto exige por FK Restrict a Usuarios — mismo patrón que
    /// GastosEndpointTests.SeedMaestrosAsync.
    /// </summary>
    private async Task<int> SembrarGastoAsync()
    {
        using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var proveedor = new Proveedor { Nombre = "Prov", Activo = true };
        ctx.Proveedores.Add(proveedor);
        var fuente = new FuenteFinanciamiento { Nombre = "Fuente", Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        var rubro = new RubroGasto { Nombre = "Rubro", Activo = true };
        ctx.RubrosGasto.Add(rubro);
        await ctx.SaveChangesAsync();

        var gasto = new Gasto
        {
            ProveedorId = proveedor.Id, Detalle = "Test", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = fuente.Id, RubroGastoId = rubro.Id,
            CondicionPago = CondicionPago.Contado,
        };
        ctx.Gastos.Add(gasto);
        await ctx.SaveChangesAsync();
        return gasto.Id;
    }

    /// <summary>Siembra un gasto + su PagoGasto real: AgregarAPagoAsync inserta un Adjunto
    /// con FK PagoGastoId -> PagosGasto, así que un id de Gasto no alcanza para probarlo.</summary>
    private async Task<int> SembrarPagoAsync()
    {
        var gastoId = await SembrarGastoAsync();
        using var ctx = Factory.CrearContexto();
        var pago = new PagoGasto { GastoId = gastoId, Fecha = DateTime.UtcNow, Monto = 100m, Activo = true };
        ctx.Add(pago);
        await ctx.SaveChangesAsync();
        return pago.Id;
    }

    private static MultipartFormDataContent ArmarMultipart(byte[] bytes, string nombre)
    {
        var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Add(archivo, "archivo", nombre);
        return contenido;
    }

    [Fact]
    public async Task PostAdjuntoGasto_ComoOperador_Devuelve201()
    {
        var gastoId = await SembrarGastoAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdjuntoDto>();
        Assert.Equal("factura.pdf", dto!.NombreArchivo);
        Assert.Equal(gastoId, dto.GastoId);
    }

    [Fact]
    public async Task PostAdjuntoGasto_SinToken_Devuelve401()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClient();

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjuntoGasto_MimeInvalido_Devuelve409()
    {
        var gastoId = await SembrarGastoAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(new byte[] { 0x00, 0x01 }, "malware.exe"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjuntoPago_ComoAdmin_Devuelve201()
    {
        var pagoId = await SembrarPagoAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync(
            $"/finanzas/pagos/{pagoId}/adjuntos", ArmarMultipart(BytesPdf, "recibo.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdjuntoDto>();
        Assert.Equal("recibo.pdf", dto!.NombreArchivo);
        Assert.Equal(pagoId, dto.PagoGastoId);
    }

    [Fact]
    public async Task PostAdjuntoPago_SinToken_Devuelve401()
    {
        // Spec F3: RegistrarGastos/RegistrarPagos los tienen tanto Admin como Operador — no
        // hay 403 por rol en el POST de pago. La matriz de negación de permisos ya la cubre
        // AuthorizationServiceTests (Task 6); a nivel Api alcanza con verificar 401.
        var pagoId = await SembrarPagoAsync();
        var client = Factory.CreateClient();

        var response = await client.PostAsync(
            $"/finanzas/pagos/{pagoId}/adjuntos", ArmarMultipart(BytesPdf, "recibo.pdf"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAdjuntosGasto_ListaLosActivos()
    {
        var gastoId = await SembrarGastoAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        var response = await client.GetAsync($"/finanzas/gastos/{gastoId}/adjuntos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>();
        Assert.Single(lista!);
    }

    [Fact]
    public async Task GetAdjuntosPago_ListaLosActivos()
    {
        var pagoId = await SembrarPagoAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsync($"/finanzas/pagos/{pagoId}/adjuntos", ArmarMultipart(BytesPdf, "recibo.pdf"));

        var response = await client.GetAsync($"/finanzas/pagos/{pagoId}/adjuntos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>();
        Assert.Single(lista!);
    }

    [Fact]
    public async Task GetContenido_DevuelveLosBytesOriginales()
    {
        var gastoId = await SembrarGastoAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await client.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDto>();

        var response = await client.GetAsync($"/finanzas/adjuntos/{dto!.Id}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(BytesPdf, bytes);
    }

    [Fact]
    public async Task GetContenido_Inexistente_Devuelve404()
    {
        await SembrarGastoAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/adjuntos/999999/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAdjunto_ComoOperador_HaceBajaLogica()
    {
        var gastoId = await SembrarGastoAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await client.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDto>();

        var response = await client.DeleteAsync($"/finanzas/adjuntos/{dto!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listado = await client.GetAsync($"/finanzas/gastos/{gastoId}/adjuntos");
        var lista = await listado.Content.ReadFromJsonAsync<List<AdjuntoDto>>();
        Assert.Empty(lista!);
    }

    [Fact]
    public async Task DeleteAdjunto_SinToken_Devuelve401()
    {
        var gastoId = await SembrarGastoAsync();
        var admin = ClienteAutenticado(TokenAdmin());
        var creado = await admin.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDto>();

        var response = await Factory.CreateClient().DeleteAsync($"/finanzas/adjuntos/{dto!.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
