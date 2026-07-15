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
        // sub-segundo. El claim "iat" del JWT sí se trunca a segundo entero (Unix epoch,
        // ver JwtTokenService), pero eso ya pasó ANTES de llegar acá, del lado de quien
        // emitió el token viejo. Si acá también truncáramos "ahora" a segundo, un token
        // viejo emitido en el MISMO segundo de reloj (microsegundos antes de la
        // revocación) pasaría la comparación como válido por error — exactamente el
        // caso que este diseño tiene que cubrir. Con "ahora" sin truncar, un iat
        // truncado siempre queda estrictamente antes de la revocación real si el login
        // ocurrió antes (nunca pueden "empatar" salvo coincidencia exacta a nivel de
        // tick, un caso no realista).
        _minimoAceptadoPorUsuario.AddOrUpdate(
            usuarioId,
            ahora,
            (_, actual) => ahora > actual ? ahora : actual);
    }

    public bool EsValido(int usuarioId, DateTime emitidoEn)
        => !_minimoAceptadoPorUsuario.TryGetValue(usuarioId, out var minimo)
        || emitidoEn >= minimo;
}
