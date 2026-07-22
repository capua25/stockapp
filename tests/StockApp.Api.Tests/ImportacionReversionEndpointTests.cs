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

public class ImportacionReversionEndpointTests : ApiTestBase
{
    private const int Ejercicio = 2026;

    public ImportacionReversionEndpointTests(ApiFactory factory) : base(factory) { }

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
    public async Task PostRevertir_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync($"/finanzas/importar/revertir/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRevertir_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/finanzas/importar/revertir/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostRevertir_IdInexistente_Devuelve404()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync($"/finanzas/importar/revertir/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CicloCompleto_ConfirmarRevertirConfirmarSinForzar_TerminaEn200()
    {
        // ConfirmarAsync/RevertirAsync dejan LogAuditoria con FK real a Usuarios; TokenAdmin()
        // reclama UsuarioId=1 (mismo patrón que ImportacionConfirmacionEndpointTests).
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = ClienteAutenticado(TokenAdmin());
        var confirmacion1 = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());
        var resultado1 = await confirmacion1.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>();

        var reversion = await client.PostAsync(
            $"/finanzas/importar/revertir/{resultado1!.IdImportacion}", null);
        Assert.Equal(HttpStatusCode.OK, reversion.StatusCode);

        var reversionOtraVez = await client.PostAsync(
            $"/finanzas/importar/revertir/{resultado1.IdImportacion}", null);
        Assert.Equal(HttpStatusCode.Conflict, reversionOtraVez.StatusCode);

        var confirmacion2 = await client.PostAsJsonAsync(
            "/finanzas/importar/confirmar", PayloadValido(forzar: false));
        Assert.Equal(HttpStatusCode.OK, confirmacion2.StatusCode);
    }
}
