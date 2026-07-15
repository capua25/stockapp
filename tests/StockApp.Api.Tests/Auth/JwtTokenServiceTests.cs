using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using StockApp.Api.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options =
        new("clave-de-prueba-de-al-menos-32-caracteres-1234567890", TimeSpan.FromHours(10));

    [Fact]
    public void GenerarToken_IncluyeClaimsDeUsuarioIdYRol()
    {
        var service = new JwtTokenService(Options);

        var token = service.GenerarToken(42, RolUsuario.Admin);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == StockAppClaimTypes.UsuarioId).Value);
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == StockAppClaimTypes.Rol).Value);
    }

    [Fact]
    public void GenerarToken_VenceEnDiezHoras()
    {
        var service = new JwtTokenService(Options);
        var antes = DateTime.UtcNow;

        var token = service.GenerarToken(1, RolUsuario.Operador);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var vencimientoEsperado = antes.Add(Options.Expiracion);
        Assert.True(Math.Abs((jwt.ValidTo - vencimientoEsperado).TotalMinutes) < 1);
    }

    // Fase B hardening: el claim "iat" es lo que permite a IRevocadorTokens invalidar
    // tokens viejos tras un reset de contraseña — sin él, no hay forma de comparar el
    // instante de emisión contra el mínimo aceptado.
    [Fact]
    public void GenerarToken_IncluyeClaimIatCercanoAAhora()
    {
        var service = new JwtTokenService(Options);
        var antes = DateTime.UtcNow;

        var token = service.GenerarToken(1, RolUsuario.Operador);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var iatClaim = jwt.Claims.Single(c => c.Type == "iat").Value;
        var iat = DateTimeOffset.FromUnixTimeSeconds(long.Parse(iatClaim)).UtcDateTime;
        Assert.True(Math.Abs((iat - antes).TotalSeconds) < 5);
    }
}
