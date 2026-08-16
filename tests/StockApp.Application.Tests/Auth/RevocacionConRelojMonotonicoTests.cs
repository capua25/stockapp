using StockApp.Application.Auth;
using Xunit;

namespace StockApp.Application.Tests.Auth;

/// <summary>
/// Cubre la composición REAL entre <see cref="RelojMonotonico"/> (emisión + revocación
/// comparten la misma instancia, tal como en Program.cs) y
/// <see cref="RevocadorTokensEnMemoria"/> — los tres escenarios pedidos para el hardening
/// del 401 espurio por salto de reloj de pared bajo NTP:
/// (a) emisión en el mismo milisegundo que la revocación,
/// (b) salto de reloj de pared hacia atrás simulado entre revocación y emisión,
/// (c) la semántica de revocación normal (emitido antes → rechazado) no se afloja.
///
/// El "iat" se reconstruye con la MISMA truncación a milisegundo que
/// JwtTokenService/OnTokenValidated (ToUnixTimeMilliseconds → FromUnixTimeMilliseconds),
/// para que estos tests ejerciten la correspondencia real, no una aproximación.
/// </summary>
public class RevocacionConRelojMonotonicoTests
{
    private const int UsuarioId = 1;

    private static DateTime ComoIat(DateTime instante)
        => DateTimeOffset.FromUnixTimeMilliseconds(
            new DateTimeOffset(instante).ToUnixTimeMilliseconds()).UtcDateTime;

    [Fact]
    public void TokenEmitidoEnElMismoMilisegundoQueLaRevocacion_SigueValido()
    {
        var monotonico = 0L;
        var reloj = new RelojMonotonico(() => DateTime.UtcNow, () => monotonico);
        var revocador = new RevocadorTokensEnMemoria();

        // Revocación y emisión caen en el MISMO tick monotónico (mismo milisegundo): no
        // hay forma de distinguir cuál pasó "primero" a esta resolución -- mismo trade-off
        // ya aceptado en RevocadorTokensEnMemoria para el truncado de iat.
        revocador.Revocar(UsuarioId, reloj.AhoraUtc());
        var emitidoEn = ComoIat(reloj.AhoraUtc());

        Assert.True(revocador.EsValido(UsuarioId, emitidoEn));
    }

    [Fact]
    public void SaltoDeRelojDeParedHaciaAtrasEntreRevocacionYEmision_TokenNuevoSigueValido()
    {
        var relojDeParedDelSistema = DateTime.UtcNow;
        var monotonico = 0L;
        // Ambos lados (revocación y emisión) comparten la MISMA instancia -- igual que
        // Program.cs registra IRelojMonotonico como singleton único para los dos.
        var reloj = new RelojMonotonico(() => relojDeParedDelSistema, () => monotonico);
        var revocador = new RevocadorTokensEnMemoria();

        // Reset de contraseña: revoca usando el reloj monotónico.
        revocador.Revocar(UsuarioId, reloj.AhoraUtc());

        // Entre la revocación y el login siguiente pasan 10ms de tiempo REAL (monotónico)...
        monotonico += 10;
        // ...pero, bajo saturación de CPU, CLOCK_REALTIME del sistema operativo saltó 64s
        // hacia atrás en el medio -- el defecto que este cambio corrige. Como `reloj` nunca
        // vuelve a leer la pared, el salto no llega a filtrarse al iat del token nuevo.
        relojDeParedDelSistema = relojDeParedDelSistema.AddSeconds(-64);

        // Login posterior a la revocación: JwtTokenService.GenerarToken usaría este mismo
        // AhoraUtc() para el claim "iat".
        var emitidoEn = ComoIat(reloj.AhoraUtc());

        Assert.True(revocador.EsValido(UsuarioId, emitidoEn),
            "un token emitido DESPUÉS de la revocación no debe rechazarse por un salto " +
            "de reloj de pared que no afecta al reloj monotónico");
    }

    [Fact]
    public void TokenEmitidoAntesDeLaRevocacion_SigueSiendoRechazado_AunConSaltoPosterior()
    {
        var relojDeParedDelSistema = DateTime.UtcNow;
        var monotonico = 0L;
        var reloj = new RelojMonotonico(() => relojDeParedDelSistema, () => monotonico);
        var revocador = new RevocadorTokensEnMemoria();

        // Token emitido ANTES de la revocación (sesión vieja).
        var emitidoEn = ComoIat(reloj.AhoraUtc());

        monotonico += 10;
        revocador.Revocar(UsuarioId, reloj.AhoraUtc());

        // Un salto de reloj de pared DESPUÉS de la revocación (ej. mientras se procesa la
        // siguiente request) no debe aflojar la revocación ya registrada -- el punto (c):
        // la semántica de revocación normal no se debilita por el cambio.
        monotonico += 5_000;
        relojDeParedDelSistema = relojDeParedDelSistema.AddSeconds(-64);

        Assert.False(revocador.EsValido(UsuarioId, emitidoEn),
            "un token emitido ANTES de la revocación tiene que seguir rechazado, sin " +
            "importar qué haga el reloj de pared después");
    }
}
