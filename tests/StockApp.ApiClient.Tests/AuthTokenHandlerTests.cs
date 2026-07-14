using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuthTokenHandlerTests
{
    private static ApiSession SesionConToken(string token = "tok-123")
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, null), token);
        return session;
    }

    [Fact]
    public async Task ConToken_AdjuntaAuthorizationBearer()
    {
        var session = SesionConToken();
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake, session);

        await http.GetAsync("categorias");

        Assert.Equal("Bearer", fake.UltimaRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", fake.UltimaRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SinToken_NoAdjuntaHeader()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake);

        await http.GetAsync("categorias");

        Assert.Null(fake.UltimaRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Un401ConToken_CierraSesionYDisparaElEvento()
    {
        var session = SesionConToken();
        var disparado = false;
        session.SesionVencida += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var http = TestHttp.CrearCliente(fake, session);

        var response = await http.GetAsync("productos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(disparado);
        Assert.False(session.EstaAutenticado);
        Assert.Null(session.Token);
    }

    [Fact]
    public async Task Un401SinToken_NoDisparaElEvento()
    {
        // POST /auth/login con credenciales inválidas devuelve 401 sin que hubiera token:
        // eso NO es sesión vencida (lo maneja AuthApiClient como LoginResult.Fallo).
        var session = new ApiSession();
        var disparado = false;
        session.SesionVencida += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var http = TestHttp.CrearCliente(fake, session);

        await http.PostAsync("auth/login", null);

        Assert.False(disparado);
    }

    [Fact]
    public async Task Un200ConToken_NoDisparaElEvento()
    {
        var session = SesionConToken();
        var disparado = false;
        session.SesionVencida += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake, session);

        await http.GetAsync("categorias");

        Assert.False(disparado);
        Assert.True(session.EstaAutenticado);
    }
}
