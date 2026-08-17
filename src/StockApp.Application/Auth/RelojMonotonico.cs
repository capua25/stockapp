using System.Diagnostics;

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
/// FUENTE MONÓTONA: <c>Stopwatch.GetTimestamp()</c>, NO <c>Environment.TickCount64</c>. Las
/// dos son monótonas, pero NO tienen la misma resolución, y la diferencia importa. En Linux
/// <c>Environment.TickCount64</c> se respalda en <c>CLOCK_MONOTONIC_COARSE</c>, que sólo
/// avanza una vez por tick del kernel: en esta máquina (WSL2) se MIDIÓ que avanza a saltos
/// de 3-4 ms, nunca de 1 ms. Como la comparación de revocación es
/// <c>emitidoEn &gt;= minimo</c>, esa granularidad abría un agujero de ~4 ms: un token
/// emitido y una revocación posterior que cayeran en el MISMO tick grueso obtenían el mismo
/// <c>AhoraUtc()</c> exacto, y el token sobrevivía a su propia revocación. Medido en esta
/// máquina: ~5 % de las bajas de usuario dejaban el token viejo vivo (era el fallo
/// intermitente de <c>DeleteUsuario_ConTokenAdmin_RevocaElTokenViejoDelUsuarioDeshabilitado</c>,
/// que devolvía 403 -- token aceptado, rol insuficiente -- en vez de 401 -- token revocado).
/// <c>Stopwatch.GetTimestamp()</c> se respalda en <c>CLOCK_MONOTONIC</c> (no COARSE), con
/// resolución de nanosegundos, y conserva intacta la propiedad que motivó esta clase: NTP
/// no lo mueve hacia atrás.
///
/// COSTO ACEPTADO (el residual, ahora sí de 1 ms y no de 4): dos eventos que caen en el
/// mismo MILISEGUNDO siguen comparando como "válido". Ya no es culpa del reloj sino del
/// formato del claim "iat", que es de milisegundos enteros -- exactamente el mismo trade-off
/// ya documentado y aceptado en <c>RevocadorTokensEnMemoria.Revocar</c> para su truncado. No
/// se cierra subiendo la precisión de "iat" ni pasando la comparación a <c>&gt;</c> estricto:
/// eso reabriría el 401 espurio que arregló 4293c6b.
/// </summary>
public sealed class RelojMonotonico : IRelojMonotonico
{
    private readonly DateTime _anclaPared;
    private readonly long _anclaMonotonica;
    private readonly Func<long> _lecturaMonotonicaActual;
    private readonly double _ticksDeTimeSpanPorUnidadMonotonica;

    public RelojMonotonico()
        : this(() => DateTime.UtcNow, Stopwatch.GetTimestamp,
               (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)
    {
    }

    /// <summary>
    /// Constructor de test: permite inyectar lecturas de pared/monótono controladas, para
    /// simular DETERMINÍSTICAMENTE un salto de reloj de pared hacia atrás, sin depender de
    /// un salto real del sistema operativo (que en producción solo se midió bajo
    /// saturación real de CPU). La lectura monótona inyectada se interpreta en
    /// MILISEGUNDOS: la unidad de <c>Stopwatch</c> es específica de la plataforma y no
    /// sirve para escribir expectativas legibles en un test.
    /// </summary>
    public RelojMonotonico(Func<DateTime> lecturaPared, Func<long> lecturaMonotonica)
        : this(lecturaPared, lecturaMonotonica, TimeSpan.TicksPerMillisecond)
    {
    }

    private RelojMonotonico(
        Func<DateTime> lecturaPared,
        Func<long> lecturaMonotonica,
        double ticksDeTimeSpanPorUnidadMonotonica)
    {
        _lecturaMonotonicaActual = lecturaMonotonica;
        _ticksDeTimeSpanPorUnidadMonotonica = ticksDeTimeSpanPorUnidadMonotonica;

        // El ancla se toma UNA SOLA VEZ, acá, en la construcción -- lecturaPared() nunca
        // se vuelve a invocar después de esto. Es la garantía ESTRUCTURAL de que ningún
        // salto de CLOCK_REALTIME posterior a la construcción del singleton puede afectar
        // AhoraUtc().
        _anclaPared = lecturaPared();
        _anclaMonotonica = lecturaMonotonica();
    }

    public DateTime AhoraUtc()
        => _anclaPared + TimeSpan.FromTicks(
            (long)((_lecturaMonotonicaActual() - _anclaMonotonica) * _ticksDeTimeSpanPorUnidadMonotonica));
}
