using StockApp.Application.Auth;
using Xunit;

namespace StockApp.Application.Tests.Auth;

public class RevocadorTokensEnMemoriaTests
{
    [Fact]
    public void EsValido_UsuarioSinRevocacion_DevuelveTrue()
    {
        var revocador = new RevocadorTokensEnMemoria();

        Assert.True(revocador.EsValido(1, DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public void Revocar_TokenEmitidoAntes_QuedaInvalido()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var emitidoEn = DateTime.UtcNow;
        var revocadoEn = emitidoEn.AddSeconds(2);

        revocador.Revocar(1, revocadoEn);

        Assert.False(revocador.EsValido(1, emitidoEn));
    }

    [Fact]
    public void Revocar_TokenEmitidoDespues_SigueValido()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var revocadoEn = DateTime.UtcNow;
        var emitidoEn = revocadoEn.AddSeconds(2);

        revocador.Revocar(1, revocadoEn);

        Assert.True(revocador.EsValido(1, emitidoEn));
    }

    [Fact]
    public void Revocar_NoAfectaAOtroUsuario()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var emitidoEn = DateTime.UtcNow;

        revocador.Revocar(1, emitidoEn.AddSeconds(2));

        Assert.True(revocador.EsValido(2, emitidoEn));
    }

    // El JWT solo tiene precisión de segundo entero (claim "iat", truncado en origen por
    // JwtTokenService), pero Revocar recibe un DateTime.UtcNow con sub-segundo. Un token
    // viejo emitido en el MISMO segundo de reloj, pero ANTES del instante exacto de
    // revocación, tiene que quedar inválido igual — comparar el iat (ya truncado en
    // origen) contra el instante preciso de revocación, sin truncar este último acá,
    // es lo que garantiza esto sin abrir una ventana de token viejo válido.
    [Fact]
    public void Revocar_TokenDelMismoSegundoPeroEmitidoAntes_QuedaInvalido()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var baseInstante = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);
        var emitidoEnTruncado = baseInstante; // así llega el iat reconstruido: sin sub-segundo
        var revocadoEn = baseInstante.AddMilliseconds(800); // mismo segundo, pero después

        revocador.Revocar(1, revocadoEn);

        Assert.False(revocador.EsValido(1, emitidoEnTruncado));
    }

    [Fact]
    public void Revocar_LlamadoDosVeces_ConservaElInstanteMasReciente()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var primero = DateTime.UtcNow;
        var segundo = primero.AddSeconds(5);
        var emitidoEntreAmbos = primero.AddSeconds(2);

        revocador.Revocar(1, segundo);
        revocador.Revocar(1, primero); // más viejo, no debería "retroceder" el mínimo

        Assert.False(revocador.EsValido(1, emitidoEntreAmbos));
    }
}
