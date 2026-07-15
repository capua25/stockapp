using System.Collections.Concurrent;

namespace StockApp.Application.Auth;

/// <summary>
/// Implementación en memoria de <see cref="IRevocadorTokens"/>. Thread-safe
/// (ConcurrentDictionary). SINGLETON: se registra una sola vez por proceso en Program.cs.
///
/// LIMITACIÓN ACEPTADA Y DOCUMENTADA: el estado se pierde al reiniciar la API — los
/// tokens emitidos antes del reset vuelven a valer hasta su expiración natural. Se acepta
/// porque es un despliegue LAN de un solo proceso, sin balanceo ni reinicios frecuentes,
/// y la expiración del JWT es corta (Jwt:ExpiracionHoras, default 12h) — no se justifica
/// la complejidad de persistir esta blacklist en la base de datos.
/// </summary>
public sealed class RevocadorTokensEnMemoria : IRevocadorTokens
{
    private readonly ConcurrentDictionary<int, DateTime> _minimoAceptadoPorUsuario = new();

    public void Revocar(int usuarioId, DateTime ahora)
    {
        // OJO con la precisión: "ahora" NO se trunca acá — se guarda tal cual, con
        // sub-segundo. El claim "iat" del JWT se emite en MILISEGUNDOS (Unix epoch,
        // ver JwtTokenService — se desvía a propósito del estándar RFC 7519, que es de
        // segundo entero) precisamente para que ambos lados de esta comparación tengan
        // la misma resolución: un login y una revocación que caigan en el mismo segundo
        // de reloj (algo frecuente bajo test o con I/O rápido) siguen siendo distinguibles.
        // Si "ahora" se truncara acá a segundo, se perdería esa precisión y un token
        // emitido milisegundos antes de la revocación (mismo segundo) podría colarse
        // como válido por error — exactamente el caso que este diseño tiene que cubrir.
        _minimoAceptadoPorUsuario.AddOrUpdate(
            usuarioId,
            ahora,
            (_, actual) => ahora > actual ? ahora : actual);
    }

    public bool EsValido(int usuarioId, DateTime emitidoEn)
        => !_minimoAceptadoPorUsuario.TryGetValue(usuarioId, out var minimo)
        || emitidoEn >= minimo;
}
