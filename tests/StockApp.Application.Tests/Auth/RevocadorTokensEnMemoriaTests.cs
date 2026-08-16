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

    // El claim "iat" tiene precisión de MILISEGUNDO (truncado en origen por
    // JwtTokenService), pero Revocar recibe un DateTime.UtcNow con sub-milisegundo. Un
    // token viejo emitido en el MISMO segundo de reloj, pero milisegundos ANTES de la
    // revocación, tiene que quedar inválido igual: truncar a milisegundo (y no a segundo)
    // conserva esa distinción.
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

    // Contracara del test de arriba, y causa raíz del 401 espurio intermitente de
    // StockApp.Api.Tests: el claim "iat" viaja en MILISEGUNDOS ENTEROS
    // (JwtTokenService usa ToUnixTimeMilliseconds, que trunca hacia abajo), mientras que
    // Revocar guardaba el instante con la precisión completa de DateTime (100 ns). Con esa
    // asimetría, un token emitido DESPUÉS de la revocación pero dentro del MISMO
    // milisegundo vuelve del decode con el iat truncado al piso de ese milisegundo —
    // es decir, ANTERIOR al mínimo aceptado — y EsValido lo rechaza: 401 espurio.
    // Ambos lados de la comparación tienen que estar en la misma resolución.
    [Fact]
    public void Revocar_TokenEmitidoDespuesEnElMismoMilisegundo_SigueValido()
    {
        var revocador = new RevocadorTokensEnMemoria();

        // Un DateTime.UtcNow real casi nunca cae en un milisegundo exacto: acá cae a mitad.
        var revocadoEn = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc).AddTicks(5_000);
        revocador.Revocar(1, revocadoEn);

        // Token emitido DESPUÉS de la revocación...
        var emitidoEnReal = revocadoEn.AddTicks(2_000);
        // ...pero así es como vuelve tras el round-trip del claim iat en milisegundos.
        var emitidoEnSegunElIat = DateTimeOffset
            .FromUnixTimeMilliseconds(new DateTimeOffset(emitidoEnReal).ToUnixTimeMilliseconds())
            .UtcDateTime;

        Assert.True(revocador.EsValido(1, emitidoEnSegunElIat));
    }

    // Necesario para aislar los tests de integración: el revocador es SINGLETON y vive toda
    // la collection "Api", mientras que ApiTestBase hace TRUNCATE ... RESTART IDENTITY, así
    // que los usuarioId 1 y 2 se reciclan en cada test. Sin limpiar, una revocación de un
    // test viejo queda apuntando a un usuario lógico distinto del test siguiente.
    [Fact]
    public void LimpiarTodo_BorraLasRevocacionesPrevias()
    {
        var revocador = new RevocadorTokensEnMemoria();
        var revocadoEn = DateTime.UtcNow;
        revocador.Revocar(1, revocadoEn);
        revocador.Revocar(2, revocadoEn);

        revocador.LimpiarTodo();

        Assert.Empty(revocador.ObtenerEstadoDiagnostico());
        Assert.True(revocador.EsValido(1, revocadoEn.AddSeconds(-60)));
        Assert.True(revocador.EsValido(2, revocadoEn.AddSeconds(-60)));
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
