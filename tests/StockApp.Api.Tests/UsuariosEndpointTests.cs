using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class UsuariosEndpointTests : ApiTestBase
{
    public UsuariosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── GET /usuarios ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsuarios_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsuarios_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsuarios_ConTokenAdmin_Devuelve200SinExponerHash()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.listado", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("HashContrasena", body);

        var usuarios = await response.Content.ReadFromJsonAsync<List<UsuarioDto>>();
        Assert.Contains(usuarios!, u => u.NombreUsuario == "usuario.listado");
    }

    // ── POST /usuarios ────────────────────────────────────────────────────────

    [Fact]
    public async Task PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("nuevo.usuario", "Nuevo Usuario", "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var ctx = Factory.CrearContexto();
        Assert.True(await ctx.Usuarios.AnyAsync(u => u.NombreUsuario == "nuevo.usuario"));
    }

    [Fact]
    public async Task PostUsuarios_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("otro", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── DELETE /usuarios/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUsuario_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        // Seed: Admin ocupa Id=1 (coincide con TokenAdmin()) para que la baja no sea auto-baja.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.baja", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task DeleteUsuario_AutoBaja_Devuelve409()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync("/usuarios/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── PUT /usuarios/{id}/rol ───────────────────────────────────────────────

    [Fact]
    public async Task PutRol_ConTokenAdmin_CambiaRolYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.rol", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{usuario.Id}/rol", new CambiarRolRequest(RolUsuario.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(RolUsuario.Admin, actualizado.Rol);
    }

    // ── PUT /usuarios/{id}/contrasena ────────────────────────────────────────

    [Fact]
    public async Task PutContrasena_AdminReseteandoOtroUsuario_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        // Seed: Admin ocupa Id=1 (coincide con TokenAdmin()) para que el reset no sea auto-cambio.
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.pwd", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync(
            $"/usuarios/{usuario.Id}/contrasena", new CambiarContrasenaRequest("nuevaClave123", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
