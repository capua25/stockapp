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

    // El JWT solo tiene precisión de segundo entero (claim "iat"), pero Revocar recibe un
    // DateTime.UtcNow con sub-segundo. Si no se trunca al comparar, un login legítimo
    // emitido en el MISMO segundo (pero antes, en microsegundos) que la revocación
    // quedaría rechazado por error. Ambos lados se truncan a segundo — este test fija
    // ese comportamiento.
    [Fact]
    public void Revocar_TruncaASegundo_TokenDelMismoSegundoPeroPosteriorSigueValido()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var baseInstante = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);
        var revocadoEn = baseInstante.AddMilliseconds(800);
        var emitidoEnMismoSegundo = baseInstante.AddMilliseconds(50); // trunca al mismo segundo

        revocador.Revocar(1, revocadoEn);

        Assert.True(revocador.EsValido(1, emitidoEnMismoSegundo));
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
