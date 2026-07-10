using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class UsuarioApiClientTests
{
    [Fact]
    public async Task Listar_GETUsuarios_DevuelveLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, nombreUsuario = "admin", nombreCompleto = (string?)null,
                rol = 0, activo = true, fechaAlta = "2026-07-01T10:00:00Z",
            },
        }));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var usuarios = await client.ListarAsync();

        Assert.Equal("/usuarios", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var u = Assert.Single(usuarios);
        Assert.Equal("admin", u.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, u.Rol);
        Assert.True(u.Activo);
    }

    [Fact]
    public async Task Alta_POSTUsuarios_DevuelveElIdDel201()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 4 }, HttpStatusCode.Created));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaUsuarioAsync("oper1", "Operario Uno", "secreto123", RolUsuario.Operador);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/usuarios", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombreUsuario\":\"oper1\"", fake.UltimoBody);
        Assert.Contains("\"contrasenaPlan\":\"secreto123\"", fake.UltimoBody);
        Assert.Contains("\"rol\":1", fake.UltimoBody); // enum numérico
        Assert.Equal(4, id);
    }

    [Fact]
    public async Task Baja_DELETEUsuariosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/usuarios/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CambiarRol_PUTUsuariosIdRol()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarRolAsync(4, RolUsuario.Admin);

        Assert.Equal("/usuarios/4/rol", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"nuevoRol\":0", fake.UltimoBody);
    }

    [Fact]
    public async Task CambiarContrasena_PUTUsuariosIdContrasena()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarContrasenaAsync(4, "nueva123", "vieja123");

        Assert.Equal("/usuarios/4/contrasena", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"nuevaContrasena\":\"nueva123\"", fake.UltimoBody);
        Assert.Contains("\"contrasenaActual\":\"vieja123\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Alta_403_LanzaUnauthorizedAccess()
    {
        // Operador intentando gestionar usuarios: la política HTTP responde 403.
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.AltaUsuarioAsync("x", null, "secreto123", RolUsuario.Operador));
    }

    [Fact]
    public async Task Baja_409_UltimoAdmin_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede dar de baja al último Admin activo."));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.BajaLogicaAsync(1));

        Assert.Equal("No se puede dar de baja al último Admin activo.", ex.Message);
    }
}
