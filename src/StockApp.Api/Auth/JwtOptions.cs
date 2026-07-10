namespace StockApp.Api.Auth;

/// <summary>
/// Secreto de firma y tiempo de vida del JWT. El secreto viene de configuración
/// (user-secrets en desarrollo; variable de entorno o secret store en producción —
/// fuera de alcance de 2a, ver spec §2). Expiracion es configurable vía
/// Jwt:ExpiracionHoras, default 12h (Fase 3a, D10).
/// </summary>
public record JwtOptions(string Secret, TimeSpan Expiracion);
