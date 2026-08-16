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
        // PRECISIÓN: se trunca a MILISEGUNDO ENTERO, que es exactamente la resolución del
        // otro lado de la comparación. El claim "iat" del JWT viaja en milisegundos Unix
        // (ver JwtTokenService: ToUnixTimeMilliseconds se desvía a propósito del RFC 7519,
        // que es de segundo entero) y ToUnixTimeMilliseconds TRUNCA HACIA ABAJO. O sea:
        // el instante de emisión que EsValido recibe es siempre MENOR O IGUAL al instante
        // real en que el token se firmó, hasta por 0,9999 ms.
        //
        // Guardar "ahora" con la precisión completa de DateTime (100 ns) generaba una
        // asimetría: un token emitido DESPUÉS de la revocación, pero dentro del mismo
        // milisegundo, volvía del decode con un iat anterior al mínimo aceptado y se
        // rechazaba — 401 con un token legítimo recién emitido. Ésa era la causa raíz del
        // 401 espurio intermitente de StockApp.Api.Tests.
        //
        // El costo de truncar es que un token emitido en el MISMO milisegundo que la
        // revocación sigue valiendo. Es el mínimo inevitable: el formato de "iat" no puede
        // representar nada más fino que el milisegundo, así que no hay forma de distinguir
        // esos dos casos. Sigue cubierto lo que importa — un token de un milisegundo
        // anterior o más queda invalidado —, y se prefiere fallar hacia "válido" por menos
        // de 1 ms antes que rechazar sesiones legítimas.
        var ahoraEnMilisegundos = ahora.AddTicks(-(ahora.Ticks % TimeSpan.TicksPerMillisecond));

        _minimoAceptadoPorUsuario.AddOrUpdate(
            usuarioId,
            ahoraEnMilisegundos,
            (_, actual) => ahoraEnMilisegundos > actual ? ahoraEnMilisegundos : actual);
    }

    public bool EsValido(int usuarioId, DateTime emitidoEn)
        => !_minimoAceptadoPorUsuario.TryGetValue(usuarioId, out var minimo)
        || emitidoEn >= minimo;

    public IReadOnlyDictionary<int, DateTime> ObtenerEstadoDiagnostico()
        => new Dictionary<int, DateTime>(_minimoAceptadoPorUsuario);

    /// <summary>
    /// Borra todas las revocaciones. NO se usa en producción — está sólo para aislar los
    /// tests de integración, y por eso vive en la clase concreta y no en
    /// <see cref="IRevocadorTokens"/>: nada del código de producción puede invocarla.
    ///
    /// Motivo: este revocador es SINGLETON y vive durante toda la collection "Api", pero
    /// ApiTestBase hace TRUNCATE ... RESTART IDENTITY antes de cada test, así que los
    /// usuarioId (1, 2, 3…) se reciclan entre usuarios lógicos completamente distintos. Una
    /// revocación dejada por un test viejo queda apuntando al usuario de otro test y lo
    /// rechaza con un 401 espurio. Es el mismo criterio con el que ApiTestBase ya resetea
    /// EstadoLicencia y con el que ApiFactory baja IProveedorPermisos a Scoped.
    /// </summary>
    public void LimpiarTodo() => _minimoAceptadoPorUsuario.Clear();
}
