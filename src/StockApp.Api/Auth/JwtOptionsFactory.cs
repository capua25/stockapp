using Microsoft.Extensions.Configuration;

namespace StockApp.Api.Auth;

/// <summary>
/// Arma JwtOptions leyendo Jwt:Secret (obligatorio) y Jwt:ExpiracionHoras (opcional,
/// default 12 — Fase 3a, D10: jornada de trabajo típica). Extraído de Program.cs para
/// poder testearlo sin levantar el host completo (mismo espíritu que DomainExceptionHandler).
/// </summary>
public static class JwtOptionsFactory
{
    public const double ExpiracionHorasPorDefecto = 12;

    public static JwtOptions Crear(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "Falta 'Jwt:Secret' en la configuración. En desarrollo: " +
                "dotnet user-secrets set \"Jwt:Secret\" \"<clave-de-al-menos-32-caracteres>\".");

        var horas = configuration.GetValue<double?>("Jwt:ExpiracionHoras") ?? ExpiracionHorasPorDefecto;

        return new JwtOptions(secret, TimeSpan.FromHours(horas));
    }
}
