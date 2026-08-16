namespace StockApp.Application.Auth;

/// <summary>
/// Reloj compartido entre la emisión de JWT (<c>JwtTokenService</c>) y la marca de
/// revocación (<see cref="IRevocadorTokens.Revocar"/>). Ver <see cref="RelojMonotonico"/>
/// para el razonamiento completo de por qué existe.
/// </summary>
public interface IRelojMonotonico
{
    /// <summary>
    /// Instante actual "de pared" (mismo formato que <c>DateTime.UtcNow</c>, para que el
    /// claim "iat" del JWT siga siendo un Unix-ms legible), pero cuyo AVANCE está
    /// gobernado por un reloj monótono: nunca retrocede, ni siquiera si el reloj de pared
    /// real del sistema operativo lo hace (ajuste de NTP bajo carga).
    /// </summary>
    DateTime AhoraUtc();
}
