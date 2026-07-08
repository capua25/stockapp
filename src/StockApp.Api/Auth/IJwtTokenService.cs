using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

public interface IJwtTokenService
{
    /// <summary>Firma un JWT con claims usuarioId y rol, vencimiento según JwtOptions.Expiracion.</summary>
    string GenerarToken(int usuarioId, RolUsuario rol);
}
