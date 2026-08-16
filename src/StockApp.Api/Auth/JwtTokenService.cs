using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly IRelojMonotonico _reloj;

    public JwtTokenService(JwtOptions options, IRelojMonotonico reloj)
    {
        _options = options;
        _reloj = reloj;
    }

    public string GenerarToken(int usuarioId, RolUsuario rol)
    {
        // Hardening del hardening (post-4293c6b): "ahora" ya NO sale de DateTime.UtcNow
        // directo -- sale de IRelojMonotonico.AhoraUtc(), la MISMA instancia compartida
        // con IRevocadorTokens (ver comentario completo en RelojMonotonico), para que un
        // salto de reloj de pared bajo carga no pueda desalinear "iat" respecto de la
        // marca de revocación. Ver RelojMonotonico para el porqué completo.
        var ahora = _reloj.AhoraUtc();

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
