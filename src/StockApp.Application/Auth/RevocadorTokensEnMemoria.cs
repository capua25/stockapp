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
        // El claim "iat" del JWT solo tiene precisión de segundo entero (Unix epoch).
        // Truncar acá a segundo evita que un login legítimo, emitido en el MISMO
        // segundo que la revocación pero en microsegundos posteriores, quede rechazado
        // por error al comparar contra un "ahora" con sub-segundo (ver EsValido).
        var minimo = TruncarASegundo(ahora);

        _minimoAceptadoPorUsuario.AddOrUpdate(
            usuarioId,
            minimo,
            (_, actual) => minimo > actual ? minimo : actual);
    }

    public bool EsValido(int usuarioId, DateTime emitidoEn)
        => !_minimoAceptadoPorUsuario.TryGetValue(usuarioId, out var minimo)
        || emitidoEn >= minimo;

    private static DateTime TruncarASegundo(DateTime valor)
        => valor.AddTicks(-(valor.Ticks % TimeSpan.TicksPerSecond));
}
