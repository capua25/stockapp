using Microsoft.AspNetCore.Http;
using StockApp.Api.Auth;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class HttpCurrentSessionTests
{
    private static HttpCurrentSession Crear()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new HttpCurrentSession(accessor);
    }

    [Fact]
    public void PermisosActuales_AntesDePoblar_DevuelveConjuntoVacio()
    {
        var session = Crear();

        Assert.Empty(session.PermisosActuales);
    }

    [Fact]
    public void EstablecerPermisos_PueblaPermisosActuales()
    {
        var session = Crear();

        session.EstablecerPermisos(new HashSet<string> { "finanzas.ver" });

        Assert.Contains("finanzas.ver", session.PermisosActuales);
    }
}
