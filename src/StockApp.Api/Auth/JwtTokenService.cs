using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options) => _options = options;

    public string GenerarToken(int usuarioId, RolUsuario rol)
    {
        var ahora = DateTime.UtcNow;

        // Claim "iat" (Fase B hardening): sin este claim, IRevocadorTokens no tiene forma
        // de comparar el instante de emisión del token contra el mínimo aceptado tras un
        // reset de contraseña. JwtSecurityToken NO lo agrega automáticamente.
        //
        // Milisegundos, no segundos: el "iat" estándar de JWT (NumericDate, RFC 7519) es
        // de precisión de SEGUNDO entero. Con esa precisión, un login y una revocación
        // que caigan en el mismo segundo de reloj (algo que pasa seguido bajo test o con
        // I/O rápido) hacen que la comparación de IRevocadorTokens no pueda distinguir de
        // forma confiable cuál pasó primero — o se cuela un token viejo, o se rechaza un
        // login legítimo recién emitido. Este token no tiene consumidores externos (solo
        // esta misma API lo firma y lo valida), así que se acepta desviarse de la
        // convención de segundos y usar milisegundos: mismo claim "iat", mucha más
        // precisión, sin romper nada que dependa de su semántica RFC.
        var claims = new[]
        {
            new Claim(StockAppClaimTypes.UsuarioId, usuarioId.ToString()),
            new Claim(StockAppClaimTypes.Rol, rol.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(ahora).ToUnixTimeMilliseconds().ToString(), ClaimValueTypes.Integer64),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: ahora.Add(_options.Expiracion),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
