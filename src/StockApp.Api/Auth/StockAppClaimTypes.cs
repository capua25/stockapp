namespace StockApp.Api.Auth;

/// <summary>
/// Nombres de claim del JWT de 2a. Centralizados acá para que quien firma el token
/// (JwtTokenService) y quien lo lee (HttpCurrentSession, las políticas de
/// autorización en Program.cs) usen exactamente el mismo string — evita drift
/// entre escritor y lector de claims.
/// </summary>
public static class StockAppClaimTypes
{
    public const string UsuarioId = "usuarioId";
    public const string Rol = "rol";
}
