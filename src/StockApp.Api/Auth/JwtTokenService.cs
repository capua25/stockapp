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
        var claims = new[]
        {
            new Claim(StockAppClaimTypes.UsuarioId, usuarioId.ToString()),
            new Claim(StockAppClaimTypes.Rol, rol.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                EpochTime.GetIntDate(ahora).ToString(), ClaimValueTypes.Integer64),
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
