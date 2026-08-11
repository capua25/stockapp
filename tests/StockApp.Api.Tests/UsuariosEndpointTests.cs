using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
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
    public async Task PostUsuarios_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("otro", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201ConId()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("nuevo.usuario", "Nuevo Usuario", "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        // No existe GET /usuarios/{id}: Location debe venir null, no una ruta rota.
        Assert.Null(response.Headers.Location);
        var body = await response.Content.ReadFromJsonAsync<UsuarioCreadoResponse>();
        Assert.True(body!.Id > 0);

        await using var ctx = Factory.CrearContexto();
        var creado = await ctx.Usuarios.SingleAsync(u => u.NombreUsuario == "nuevo.usuario");
        Assert.Equal(body.Id, creado.Id);
    }

    [Fact]
    public async Task PostUsuarios_ConRolAusente_Devuelve400YNoCreaUsuarioAdmin()
    {
        // Regresión análoga a PutRol_ConBodyVacio: CrearUsuarioRequest.Rol era RolUsuario
        // no-nullable con Admin = 0. Un body sin el campo "rol" dejaba a System.Text.Json
        // poner el default del tipo — 0 = Admin — y el POST creaba un usuario Admin nuevo
        // en silencio con 201 Created. Este test es el único que impide que el agujero
        // vuelva: si Rol vuelve a ser no-nullable, este test da 201 con un usuario Admin
        // en vez de 400.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/usuarios",
            new StringContent("""{"nombreUsuario":"usuario.sinrol","contrasenaPlan":"pwd12345"}""",
                System.Text.Encoding.UTF8, "application/json"));

        await using var verificacion = Factory.CrearContexto();
        var creado = await verificacion.Usuarios.SingleOrDefaultAsync(u => u.NombreUsuario == "usuario.sinrol");
        Assert.Null(creado);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostUsuarios_ConRolFueraDelEnum_Devuelve400YNoCreaUsuario()
    {
        // Hallazgo 1: sin JsonStringEnumConverter, {"rol":99} deserializa a (RolUsuario)99
        // y pasaba el chequeo de solo-null en el endpoint. Este test es el que impide que
        // vuelva a colarse una fila con Rol fuera del enum.
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/usuarios",
            new StringContent("""{"nombreUsuario":"usuario.rolinvalido","contrasenaPlan":"pwd12345","rol":99}""",
                System.Text.Encoding.UTF8, "application/json"));

        await using var verificacion = Factory.CrearContexto();
        var creado = await verificacion.Usuarios.SingleOrDefaultAsync(u => u.NombreUsuario == "usuario.rolinvalido");
        Assert.Null(creado);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    [Fact]
    public async Task PostUsuarios_ConNombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "duplicado", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("duplicado", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Contains("duplicado", doc.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PostUsuarios_ConNombreVacio_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostUsuarios_ConNombreSoloWhitespace_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("   ", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostUsuarios_ConNombreDe101Caracteres_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var nombreDe101Caracteres = new string('a', 101);
        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest(nombreDe101Caracteres, null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostUsuarios_ConContrasenaMenorA6Caracteres_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("usuario.pwdcorta", null, "abc12", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostUsuarios_ConEspaciosAlBorde_Devuelve201YElNombreQuedaTrimeado()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("  espacios.borde  ", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var ctx = Factory.CrearContexto();
        var creado = await ctx.Usuarios.SingleAsync(u => u.NombreUsuario == "espacios.borde");
        Assert.Equal("espacios.borde", creado.NombreUsuario);
    }

    // ── DELETE /usuarios/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUsuario_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.DeleteAsync("/usuarios/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUsuario_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.DeleteAsync("/usuarios/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

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
    public async Task DeleteUsuario_ConTokenAdmin_RevocaElTokenViejoDelUsuarioDeshabilitado()
    {
        // Deuda M3 (hardening Fase B): el usuario deshabilitado no debe poder seguir
        // usando su JWT viejo hasta que expire naturalmente.
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();

        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.baja.revoca", "Secreta123!", RolUsuario.Operador);

        var tokenViejoDelUsuario = jwt.GenerarToken(usuario.Id, RolUsuario.Operador);

        var clienteAdmin = Factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());
        var bajaResponse = await clienteAdmin.DeleteAsync($"/usuarios/{usuario.Id}");
        Assert.Equal(HttpStatusCode.OK, bajaResponse.StatusCode);

        var clienteUsuarioBaja = Factory.CreateClient();
        clienteUsuarioBaja.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenViejoDelUsuario);
        var response = await clienteUsuarioBaja.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
    public async Task PutRol_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PutAsJsonAsync("/usuarios/1/rol", new CambiarRolRequest(RolUsuario.Admin));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutRol_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PutAsJsonAsync("/usuarios/1/rol", new CambiarRolRequest(RolUsuario.Admin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

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

    [Fact]
    public async Task PutRol_ConBodyVacio_Devuelve400YNoPromueveANadieAAdmin()
    {
        // Regresión del agujero más grave del módulo: CambiarRolRequest tenía NuevoRol
        // no-nullable con RolUsuario.Admin = 0. Un body {} (o sin el campo) dejaba a
        // System.Text.Json poner el default del tipo — 0 = Admin — y el usuario quedaba
        // promovido en silencio con 200 OK. Este test es el único que impide que el
        // agujero vuelva: si NuevoRol vuelve a ser no-nullable, este test da 200 en vez
        // de 400.
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.sinrol", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsync($"/usuarios/{usuario.Id}/rol",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(RolUsuario.Operador, actualizado.Rol);
    }

    [Fact]
    public async Task PutRol_ConValorFueraDelEnum_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.rolinvalido", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsync($"/usuarios/{usuario.Id}/rol",
            new StringContent("""{"nuevoRol":99}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Hallazgo 5: si el chequeo del enum se mueve a después del ActualizarAsync, este
        // test debe quedar rojo con la fila ya corrupta, no verde mirando solo el status.
        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(RolUsuario.Operador, actualizado.Rol);
    }

    [Fact]
    public async Task PutRol_DegradandoAlUltimoAdminActivo_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        var admin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.unico", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{admin.Id}/rol",
            new CambiarRolRequest(RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == admin.Id);
        Assert.Equal(RolUsuario.Admin, actualizado.Rol);
    }

    // ── PUT /usuarios/{id}/contrasena ────────────────────────────────────────

    [Fact]
    public async Task PutContrasena_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            "/usuarios/1/contrasena", new CambiarContrasenaRequest("nuevaClave123", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutContrasena_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PutAsJsonAsync(
            "/usuarios/1/contrasena", new CambiarContrasenaRequest("nuevaClave123", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

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

        // Verifica persistencia real del hash: lee desde DB con contexto fresco.
        await using var verificacion = Factory.CrearContexto();
        var usuarioActualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);

        // Resuelve el hasher desde el DI del API.
        var hasher = Factory.Services.GetRequiredService<IPasswordHasher>();

        // La contraseña NUEVA debe verificar contra el hash persistido.
        Assert.True(hasher.Verify("nuevaClave123", usuarioActualizado.HashContrasena),
            "La contraseña nueva no verifica contra el hash persistido.");

        // La contraseña VIEJA ya NO debe verificar.
        Assert.False(hasher.Verify("Secreta123!", usuarioActualizado.HashContrasena),
            "La contraseña vieja verifica contra el nuevo hash (bug: contraseña no se cambió).");
    }

    // ── GET/PUT /usuarios/{id}/permisos (spec 2026-08-10) ────────────────────

    [Fact]
    public async Task GetPermisos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/usuarios/1/permisos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPermisos_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/usuarios/1/permisos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutPermisos_Admin_GuardaYQuedaLegibleEnGet()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.putpermisos", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var put = await client.PutAsJsonAsync($"/usuarios/{operador.Id}/permisos",
            new { Permisos = new[] { "finanzas.ver", "catalogo.productos" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await client.GetAsync($"/usuarios/{operador.Id}/permisos");
        var body = await get.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Equal(2, body!.Permisos.Count);
        Assert.Contains("finanzas.ver", body.Permisos);
        Assert.Contains("catalogo.productos", body.Permisos);
    }

    [Fact]
    public async Task PutPermisos_ListaVacia_Devuelve200YQuedaSinPermisosEnGet()
    {
        // Camino válido: el Admin le quita TODOS los permisos configurables a un Operador.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.sinpermisos", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var put = await client.PutAsJsonAsync($"/usuarios/{operador.Id}/permisos",
            new { Permisos = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await client.GetAsync($"/usuarios/{operador.Id}/permisos");
        var body = await get.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Empty(body!.Permisos);
    }

    [Fact]
    public async Task PutPermisos_BodySinCampoPermisos_TratadoComoListaVaciaYDevuelve200()
    {
        // GuardarPermisosRequest.Permisos es nullable a propósito: un body "{}" (cliente
        // viejo o manipulado) no debe crashear con un 500 — se trata como lista vacía.
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.bodyvacio", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsync($"/usuarios/{operador.Id}/permisos",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var get = await client.GetAsync($"/usuarios/{operador.Id}/permisos");
        var body = await get.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Empty(body!.Permisos);
    }

    [Fact]
    public async Task PutPermisos_UsuarioObjetivoEsAdmin_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        var otroAdmin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.destino", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{otroAdmin.Id}/permisos",
            new { Permisos = new[] { "finanzas.ver" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutPermisos_IdInexistente_Devuelve404()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync("/usuarios/999999/permisos",
            new { Permisos = new[] { "finanzas.ver" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutPermisos_PermisoFueraDeWhitelist_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.whitelist", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{operador.Id}/permisos",
            new { Permisos = new[] { "usuarios.gestionar" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
