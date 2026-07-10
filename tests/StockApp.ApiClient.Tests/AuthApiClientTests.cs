// tests/StockApp.ApiClient.Tests/AuthApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuthApiClientTests
{
    private static readonly object LoginOkBody = new
    {
        token = "tok-1",
        usuario = new { id = 1, nombreUsuario = "admin", nombreCompleto = "Ana Admin", rol = 0 },
    };

    [Fact]
    public async Task Login_Exitoso_POSTAuthLogin_EstableceLaSesionYDevuelveOk()
    {
        var session = new ApiSession();
        var fake = new FakeHttpHandler(_ => TestHttp.Json(LoginOkBody));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        var resultado = await client.LoginAsync("admin", "admin123");

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/login", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombreUsuario\":\"admin\"", fake.UltimoBody);
        Assert.Contains("\"contrasena\":\"admin123\"", fake.UltimoBody);
        Assert.True(resultado.Exitoso);
        Assert.True(session.EstaAutenticado);
        Assert.Equal("tok-1", session.Token);
        Assert.Equal(1, session.UsuarioActual!.Id);
        Assert.Equal("Ana Admin", session.UsuarioActual.NombreCompleto);
        Assert.Equal(RolUsuario.Admin, session.RolActual);
    }

    [Fact]
    public async Task Login_401_DevuelveFalloSinEstablecerSesion()
    {
        var session = new ApiSession();
        var fake = new FakeHttpHandler(_ =>
            TestHttp.Problema(HttpStatusCode.Unauthorized, null, "Usuario o contraseña inválidos."));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        var resultado = await client.LoginAsync("admin", "mala");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.ContrasenaInvalida, resultado.Error);
        Assert.False(session.EstaAutenticado);
    }

    [Fact]
    public async Task Login_LimpiaLaSesionAnteriorAntesDeIntentar()
    {
        // Un login nuevo invalida la sesión previa y evita adjuntar un token viejo al request.
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(9, "viejo", RolUsuario.Operador, null), "tok-viejo");
        var fake = new FakeHttpHandler(_ => TestHttp.Json(LoginOkBody));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        await client.LoginAsync("admin", "admin123");

        Assert.Null(fake.UltimaRequest!.Headers.Authorization);
        Assert.Equal("tok-1", session.Token);
    }

    [Fact]
    public async Task Login_ServidorCaido_LanzaServidorNoDisponible()
    {
        var session = new ApiSession();
        var fake = new FakeHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        await Assert.ThrowsAsync<ServidorNoDisponibleException>(
            () => client.LoginAsync("admin", "admin123"));
        Assert.False(session.EstaAutenticado);
    }

    [Fact]
    public async Task Logout_CierraLaSesionSinLlamarAlServidor()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, null), "tok-1");
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        await client.LogoutAsync();

        Assert.False(session.EstaAutenticado);
        Assert.Null(session.Token);
        Assert.Null(fake.UltimaRequest); // JWT sin estado: no existe endpoint de logout.
    }
}
