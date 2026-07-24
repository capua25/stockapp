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

public class ImportacionHistorialEndpointTests : ApiTestBase
{
    private const int Ejercicio = 2026;

    public ImportacionHistorialEndpointTests(ApiFactory factory) : base(factory) { }

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

    private static ConfirmarImportacionDto PayloadMinimo(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task GetHistorial_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/importar/historial");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/finanzas/importar/historial");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ComoAdmin_SinImportaciones_Devuelve200YListaVacia()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/importar/historial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var historial = await response.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>();
        Assert.NotNull(historial);
        Assert.Empty(historial!);
    }

    [Fact]
    public async Task GetHistorial_ComoAdmin_ConImportacionConfirmadaYRevertida_ReflejaElEstado()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = ClienteAutenticado(TokenAdmin());
        var confirmacion = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadMinimo());
        var resultado = await confirmacion.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>();

        var historialActivo = await client.GetAsync("/finanzas/importar/historial");
        var listaActiva = await historialActivo.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>();
        var filaActiva = Assert.Single(listaActiva!);
        Assert.Equal(resultado!.IdImportacion, filaActiva.IdImportacion);
        Assert.False(filaActiva.Revertida);

        await client.PostAsync($"/finanzas/importar/revertir/{resultado.IdImportacion}", null);

        var historialRevertido = await client.GetAsync("/finanzas/importar/historial");
        var listaRevertida = await historialRevertido.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>();
        var filaRevertida = Assert.Single(listaRevertida!);
        Assert.True(filaRevertida.Revertida);
    }
}
