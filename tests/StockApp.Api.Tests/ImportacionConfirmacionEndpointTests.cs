using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ImportacionConfirmacionEndpointTests : ApiTestBase
{
    private const int Ejercicio = 2026;

    public ImportacionConfirmacionEndpointTests(ApiFactory factory) : base(factory) { }

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

    private static ConfirmarImportacionDto PayloadValido(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string> { "ACME SA" },
            new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado, null),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task PostConfirmar_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmar_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmar_ComoAdmin_PayloadValido_Devuelve200YResultado()
    {
        await using var ctx = Factory.CrearContexto();
        // ConfirmarAsync deja LogAuditoria con FK real a Usuarios; TokenAdmin() reclama
        // UsuarioId=1, así que sembramos Admin (Id=1) + Operador (Id=2) en ese orden (mismo
        // patrón que MovimientosEndpointTests).
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>();
        Assert.NotNull(resultado);
        Assert.Equal(1, resultado!.GastosCreados);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task PostConfirmar_ReferenciaQueNoResuelve_Devuelve400ConErrors()
    {
        var client = ClienteAutenticado(TokenAdmin());
        var payload = PayloadValido() with { MaestrosNuevos = new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }) };

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Gastos[0].Proveedor", body);
    }

    [Fact]
    public async Task PostConfirmar_SegundaVezSinForzar_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido(forzar: false));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmar_SegundaVezConForzar_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido(forzar: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
