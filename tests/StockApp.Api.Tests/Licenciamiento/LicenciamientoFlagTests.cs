using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

/// <summary>
/// Interruptor de licenciamiento opcional (Licencia:Habilitado, Program.cs). Cada test arma su
/// propio host vía Factory.WithWebHostBuilder — mismo patrón que RateLimitingTests — para pisar
/// el flag sin afectar al resto de la collection "Api", que corre con el flag ausente (default
/// true) igual que hasta ahora.
/// </summary>
public class LicenciamientoFlagTests : ApiTestBase
{
    public LicenciamientoFlagTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Deshabilitado_NingunaRutaDevuelve423()
    {
        await using var factoryDeshabilitado = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Licencia:Habilitado"] = "false" })));
        var client = factoryDeshabilitado.CreateClient();

        // Sin token -> 401 (no 423): con el flag apagado, BloqueoLicenciaMiddleware ni siquiera
        // se registra en el pipeline (Program.cs) — el request llega derecho al gate de
        // autenticación, sin pasar por el de licencia.
        var response = await client.GetAsync("/productos");

        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HabilitadoExplicito_ComportamientoActualIntacto_SigueDando423ConLicenciaInactiva()
    {
        await using var factoryHabilitado = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Licencia:Habilitado"] = "true" })));
        factoryHabilitado.Services.GetRequiredService<EstadoLicencia>().Activada = false;
        var client = factoryHabilitado.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
    }

    /// <summary>
    /// EL TEST MÁS IMPORTANTE DE ESTE ARCHIVO: custodia la regla de seguridad del diseño. Con el
    /// licenciamiento desactivado, el reset de contraseña del Admin TIENE que seguir exigiendo
    /// firma válida — ValidadorFirma es un singleton compartido entre ServicioLicencia y
    /// ServicioResetAdmin (ver el comentario en Program.cs, junto a "licenciamientoHabilitado",
    /// sobre por qué el flag NO puede tocar ese registro). Un token de reset firmado con OTRA
    /// clave privada (no la configurada en ApiFactory/ClavesDePrueba) tiene que seguir siendo
    /// rechazado, exactamente igual que con el licenciamiento activo.
    /// </summary>
    [Fact]
    public async Task Deshabilitado_ResetAdminSigueExigiendoFirmaValida()
    {
        await using var factoryDeshabilitado = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Licencia:Habilitado"] = "false" })));
        var client = factoryDeshabilitado.CreateClient();

        var desafioResponse = await client.PostAsync("/auth/reset-admin/desafio", content: null);
        Assert.Equal(HttpStatusCode.OK, desafioResponse.StatusCode);
        var desafio = (await desafioResponse.Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;

        // Firmado con una clave privada DISTINTA a la configurada (ClavesDePrueba): simula un
        // atacante que no tiene la clave privada real, aun con el licenciamiento desactivado.
        using var otraClave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var tokenMalFirmado = FirmadorLicencias.EmitirTokenReset(
            new TokenResetPayload(1, "reset-admin", ClavesDePrueba.CodigoMaquina, desafio),
            otraClave.ExportPkcs8PrivateKeyPem());

        var reset = await client.PostAsJsonAsync(
            "/auth/reset-admin", new ResetAdminRequest(tokenMalFirmado, "clave-nueva-123"));

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        var json = await reset.Content.ReadAsStringAsync();
        Assert.Contains("firma", json, StringComparison.OrdinalIgnoreCase);
    }
}
