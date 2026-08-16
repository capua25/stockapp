namespace StockApp.Application.Auth;

/// <summary>
/// Implementación de <see cref="IRelojMonotonico"/>. SINGLETON: una sola instancia por
/// proceso, compartida entre <c>JwtTokenService</c> (emisión, claim "iat") y todo llamador
/// de <see cref="IRevocadorTokens.Revocar"/> (UsuarioService, ServicioResetAdmin).
///
/// PROBLEMA (hardening del hardening de 4293c6b): la comparación de revocación de
/// <c>RevocadorTokensEnMemoria</c> (<c>emitidoEn >= minimo</c>) enfrenta dos lecturas de
/// reloj de pared con tolerancia CERO. 4293c6b arregló la asimetría de PRECISIÓN (ms vs.
/// 100ns) entre esas dos lecturas. Queda un defecto distinto: en esta máquina se MIDIÓ que
/// CLOCK_REALTIME (la fuente de <c>DateTime.UtcNow</c>) NO es monótono -- bajo saturación
/// de CPU (16 procesos, 12 cores) el reloj de pared puede saltar HACIA ATRÁS ~64s
/// (ajuste de NTP). Si ese salto cae ENTRE la lectura de una revocación y la lectura de la
/// emisión de un token más nuevo, el token nuevo sale con un "ahora" numéricamente
/// ANTERIOR a la revocación aunque en el tiempo real haya sido emitido después -- 401
/// espurio con un token legítimo. NO se resuelve con una tolerancia de reloj (ese es el
/// mecanismo del ClockSkew de 5 min de JwtBearer para expiración): una tolerancia acá
/// reabriría la ventana de revocación por ese mismo lapso -- un token recién revocado
/// seguiría aceptándose durante la tolerancia. Eso es un agujero de seguridad; un reloj
/// monótono no lo tiene.
///
/// SOLUCIÓN: anclar un instante de pared (<c>DateTime.UtcNow</c>) UNA SOLA VEZ -- en la
/// construcción de este singleton, antes de que arranque tráfico real -- y, desde ahí,
/// avanzar ese instante usando ÚNICAMENTE el reloj MONÓTONO del sistema
/// (<c>Environment.TickCount64</c>, respaldado en Linux por <c>CLOCK_MONOTONIC</c> vía
/// <c>clock_gettime</c>): un reloj que un ajuste de NTP jamás mueve hacia atrás, a
/// diferencia de <c>CLOCK_REALTIME</c>. <see cref="AhoraUtc"/> nunca vuelve a leer el
/// reloj de pared, así que ningún salto posterior de <c>CLOCK_REALTIME</c> puede afectar
/// el resultado.
///
/// CORRESPONDENCIA CON "iat" (el punto delicado del cambio): el claim "iat" del JWT sigue
/// viajando en milisegundos Unix -- formato de pared, sin cambios, ver
/// <c>JwtTokenService</c> -- pero el valor que <c>ToUnixTimeMilliseconds()</c> convierte
/// YA NO sale de <c>DateTime.UtcNow</c>: sale de <see cref="AhoraUtc"/>, es decir, del
/// ancla de pared MÁS el tiempo MONÓTONO transcurrido desde esa ancla. Es una traducción
/// de UNIDADES (monótono -> forma-de-pared), no una reinterpretación de semántica: el
/// resultado sigue siendo un <c>DateTime</c> con forma de instante real (legible/depurable,
/// compatible con el resto del pipeline sin tocar <c>RevocadorTokensEnMemoria</c> ni
/// <c>IRevocadorTokens</c>), pero su PROGRESO en el tiempo está gobernado por un reloj que
/// nunca retrocede. Emisión y revocación TIENEN que compartir la MISMA instancia -- si
/// cada una anclara por su cuenta en un momento real distinto, un salto entre esos dos
/// anclajes reintroduciría el bug -- por eso se registra como singleton único en DI
/// (Program.cs) y se inyecta en ambos lados.
///
/// COSTO ACEPTADO: dos eventos (revocación/emisión) que caen en el mismo milisegundo según
/// este reloj (resolución de <c>Environment.TickCount64</c>) siguen comparando como
/// "válido" -- exactamente el mismo trade-off ya documentado y aceptado en
/// <c>RevocadorTokensEnMemoria.Revocar</c> para el truncado de "iat": el formato no puede
/// representar nada más fino, así que no es una regresión nueva.
/// </summary>
public sealed class RelojMonotonico : IRelojMonotonico
{
    private readonly DateTime _anclaPared;
    private readonly long _anclaMonotonica;
    private readonly Func<long> _lecturaMonotonicaActual;

    public RelojMonotonico() : this(() => DateTime.UtcNow, () => Environment.TickCount64) { }

    /// <summary>
    /// Constructor de test: permite inyectar lecturas de pared/monótono controladas, para
    /// simular DETERMINÍSTICAMENTE un salto de reloj de pared hacia atrás, sin depender de
    /// un salto real del sistema operativo (que en producción solo se midió bajo
    /// saturación real de CPU).
    /// </summary>
    public RelojMonotonico(Func<DateTime> lecturaPared, Func<long> lecturaMonotonica)
    {
        _lecturaMonotonicaActual = lecturaMonotonica;

        // El ancla se toma UNA SOLA VEZ, acá, en la construcción -- lecturaPared() nunca
        // se vuelve a invocar después de esto. Es la garantía ESTRUCTURAL de que ningún
        // salto de CLOCK_REALTIME posterior a la construcción del singleton puede afectar
        // AhoraUtc().
        _anclaPared = lecturaPared();
        _anclaMonotonica = lecturaMonotonica();
    }

    public DateTime AhoraUtc()
        => _anclaPared + TimeSpan.FromMilliseconds(_lecturaMonotonicaActual() - _anclaMonotonica);
}
