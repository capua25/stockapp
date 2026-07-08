namespace StockApp.Api.Auth;

/// <summary>
/// Secreto de firma y tiempo de vida del JWT. El secreto viene de configuración
/// (user-secrets en desarrollo; variable de entorno o secret store en producción —
/// fuera de alcance de 2a, ver spec §2). Expiracion es fija en 10 horas en 2a,
/// no configurable (spec §2: revisitar en 2b/2c si hace falta ajustarla).
/// </summary>
public record JwtOptions(string Secret, TimeSpan Expiracion);
