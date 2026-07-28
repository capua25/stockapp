using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Licenciamiento;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class BloqueoLicenciaTests : ApiTestBase
{
    public BloqueoLicenciaTests(ApiFactory factory) : base(factory) { }

    private void Bloquear()
        => Factory.Services.GetRequiredService<EstadoLicencia>().Activada = false;

    [Fact]
    public async Task Bloqueada_EndpointNormal_Devuelve423()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal((HttpStatusCode)423, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(423, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Licencia no activada.", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            "El servidor no tiene una licencia válida activada. "
            + "Activá la licencia desde la pantalla de bloqueo del cliente.",
            doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Bloqueada_Login_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        // Credenciales inexistentes -> 401 de AuthEndpoints, pero NO 423: confirma que la ruta
        // atraviesa el middleware de licencia (que la dejaría pasar) y llega hasta el login real.
        var response = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "x", Contrasena = "y" });

        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_EstadoDeLicencia_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/licencia/estado");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_DesafioDeReset_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/auth/reset-admin/desafio", content: null);

        // El endpoint de reset se agrega en Task 5; acá sólo importa que el middleware NO lo bloquee.
        // Antes de Task 5 devolverá 404 (ruta inexistente), NO 423.
        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_Backups_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        // Sin token -> 401 (no 423): confirma que la ruta atraviesa el middleware de licencia
        // (que la dejaría pasar) y llega hasta el de autenticación.
        var response = await client.GetAsync("/backups");

        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
    }

    [Fact]
    public async Task Bloqueada_UnAdminSeAutenticaYObtiene200DeBackups_FlujoCompletoReal()
    {
        // Este test reemplaza al que daba verde en falso (Assert.NotEqual(423) también pasaba
        // con un 401 -- o sea, pasaba CON el bug presente, sin probar que el flujo funcionara).
        // El spec §8 pedía 200 con licencia vencida en la matriz: acá se ejercita el camino REAL
        // -- login vía HTTP (no un token generado a mano) contra un usuario Admin sembrado en la
        // base, con la licencia efectivamente bloqueada -- para probar que el fix cumple lo que
        // promete: un admin puede llegar a los backups sin licencia activa.
        await using (var ctx = Factory.CrearContexto())
        {
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.backups", "Secreta123!", RolUsuario.Admin);
        }
        Bloquear();
        var client = Factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin.backups", Contrasena = "Secreta123!" });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        var backupsResponse = await client.GetAsync("/backups");
        Assert.Equal(HttpStatusCode.OK, backupsResponse.StatusCode);

        // Acotado (mismo test, evidencia de que NO se abrió el resto del sistema): el MISMO
        // token, contra un endpoint cualquiera que no está en la lista de exenciones, sigue
        // dando 423 -- el middleware bloquea por ruta, no por identidad, así que autenticarse
        // no habilitó a operar nada más que /backups.
        var productosResponse = await client.GetAsync("/productos");
        Assert.Equal((HttpStatusCode)423, productosResponse.StatusCode);
    }

    [Fact]
    public async Task Activada_EndpointNormal_NoDevuelve423()
    {
        // ApiTestBase deja Activada=true por defecto.
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        // Sin token → 401, pero NO 423 (la licencia está activa).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
