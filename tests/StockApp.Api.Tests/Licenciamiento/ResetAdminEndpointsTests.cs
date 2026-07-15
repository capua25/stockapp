using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Licenciamiento;

public class ResetAdminEndpointsTests : ApiTestBase
{
    public ResetAdminEndpointsTests(ApiFactory factory) : base(factory) { }

    private async Task SembrarAdminAsync(string usuario, string contrasena)
    {
        // ApiTestBase.LimpiarTablas() trunca ANTES de que el host se construya; si esta
        // corrida es la primera del proceso en tocar Factory.Services, el bootstrap seeder
        // (Bootstrap:AdminUser="admin-arranque") recién crea su usuario en ese momento, después
        // del truncate. Se re-trunca acá para no depender del orden de ejecución de los tests.
        using var ctx = Factory.CrearContexto();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"Usuarios\" RESTART IDENTITY CASCADE;");

        using var scope = Factory.Services.CreateScope();
        var primerArranque = scope.ServiceProvider.GetRequiredService<IPrimerArranqueService>();
        await primerArranque.CrearAdminInicialAsync(usuario, contrasena);
    }

    [Fact]
    public async Task Desafio_DevuelveDesafioYCodigoDeMaquina()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/auth/reset-admin/desafio", content: null);
        var body = await response.Content.ReadFromJsonAsync<ResetDesafioResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body!.Desafio));
        Assert.Equal(ClavesDePrueba.CodigoMaquina, body.CodigoMaquina);
    }

    [Fact]
    public async Task Reset_FlujoCompleto_CambiaLaContrasenaYPermiteLogin()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio);

        var reset = await client.PostAsJsonAsync("/auth/reset-admin",
            new ResetAdminRequest(token, "clave-nueva-9"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // La vieja ya no sirve; la nueva loguea.
        var loginViejo = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin", Contrasena = "clave-vieja-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginViejo.StatusCode);

        var loginNuevo = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin", Contrasena = "clave-nueva-9" });
        Assert.Equal(HttpStatusCode.OK, loginNuevo.StatusCode);
    }

    [Fact]
    public async Task Reset_TokenReusado_Rechazado()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio);

        await client.PostAsJsonAsync("/auth/reset-admin", new ResetAdminRequest(token, "clave-nueva-9"));
        var segunda = await client.PostAsJsonAsync("/auth/reset-admin", new ResetAdminRequest(token, "otra-clave-8"));

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
    }

    [Fact]
    public async Task Reset_TokenDeOtraMaquina_Rechazado()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio, maquina: "OTRA-MAQUINA");

        var reset = await client.PostAsJsonAsync("/auth/reset-admin", new ResetAdminRequest(token, "clave-nueva-9"));

        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    // Fase B hardening: el JWT emitido ANTES del reset deja de servir de inmediato en
    // un endpoint protegido, sin esperar a su expiración natural; el login con la
    // contraseña nueva sigue funcionando con normalidad.
    [Fact]
    public async Task Reset_TokenViejoQuedaRevocado_YLoginNuevoFunciona()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var loginViejo = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin", Contrasena = "clave-vieja-1" });
        var tokenViejo = (await loginViejo.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        var token = ClavesDePrueba.EmitirTokenReset(desafio);
        var reset = await client.PostAsJsonAsync("/auth/reset-admin",
            new ResetAdminRequest(token, "clave-nueva-9"));
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var clienteConTokenViejo = Factory.CreateClient();
        clienteConTokenViejo.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenViejo);
        var protegidoConTokenViejo = await clienteConTokenViejo.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.Unauthorized, protegidoConTokenViejo.StatusCode);

        var loginNuevo = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "admin", Contrasena = "clave-nueva-9" });
        Assert.Equal(HttpStatusCode.OK, loginNuevo.StatusCode);

        var tokenNuevo = (await loginNuevo.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        var clienteConTokenNuevo = Factory.CreateClient();
        clienteConTokenNuevo.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokenNuevo);
        var protegidoConTokenNuevo = await clienteConTokenNuevo.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.OK, protegidoConTokenNuevo.StatusCode);
    }

    [Fact]
    public async Task Reset_Exitoso_QuedaAuditado()
    {
        await SembrarAdminAsync("admin", "clave-vieja-1");
        var client = Factory.CreateClient();

        var desafio = (await (await client.PostAsync("/auth/reset-admin/desafio", null))
            .Content.ReadFromJsonAsync<ResetDesafioResponse>())!.Desafio;
        await client.PostAsJsonAsync("/auth/reset-admin",
            new ResetAdminRequest(ClavesDePrueba.EmitirTokenReset(desafio), "clave-nueva-9"));

        using var ctx = Factory.CrearContexto();
        Assert.True(await ctx.LogsAuditoria.AnyAsync(l => l.Accion == AccionAuditada.ResetAdminFirmado));
    }
}
