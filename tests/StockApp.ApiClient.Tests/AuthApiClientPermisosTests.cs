using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuthApiClientPermisosTests
{
    [Fact]
    public async Task ObtenerPermisosPropiosAsync_GETAuthPermisos_DevuelveElSetYPueblaLaSesion()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "operador", RolUsuario.Operador, null), "tok-1");
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { permisos = new[] { "finanzas.ver" } }));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        var permisos = await client.ObtenerPermisosPropiosAsync();

        Assert.Equal("/auth/permisos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("finanzas.ver", permisos);
        Assert.Contains("finanzas.ver", session.PermisosActuales);
    }
}
