using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

/// <summary>
/// Guardián del 401 espurio intermitente (ver Parte 2 de la investigación de flakiness).
///
/// El claim "iat" viaja en MILISEGUNDOS ENTEROS: JwtTokenService lo escribe con
/// ToUnixTimeMilliseconds(), que TRUNCA hacia abajo, y OnTokenValidated (Program.cs) lo
/// reconstruye con FromUnixTimeMilliseconds(). Es decir: el instante de emisión que ve el
/// pipeline de autenticación es siempre MENOR O IGUAL al instante real de emisión, hasta
/// por 0,9999 ms.
///
/// IRevocadorTokens, en cambio, guardaba el mínimo aceptado con la precisión completa de
/// DateTime (100 ns). Con esa asimetría, un token emitido DESPUÉS de una revocación pero
/// dentro del MISMO milisegundo volvía del decode con un iat ANTERIOR al mínimo aceptado
/// y el pipeline lo rechazaba: 401 con un token legítimo recién emitido.
///
/// Este test recorre el pipeline HTTP real completo (JwtBearer + OnTokenValidated +
/// IRevocadorTokens), no sólo la aritmética del revocador.
/// </summary>
public class RevocacionPrecisionIatTests : ApiTestBase
{
    public RevocacionPrecisionIatTests(ApiFactory factory) : base(factory) { }

    // usuarioId propio y alto a propósito: el RevocadorTokensEnMemoria es SINGLETON y no se
    // resetea entre tests, mientras que ApiTestBase hace TRUNCATE ... RESTART IDENTITY (los
    // ids 1 y 2 se reciclan en toda la collection). Usar un id que ningún otro test toca
    // evita que esta revocación contamine al resto de la suite.
    private const int UsuarioIdAislado = 987_654;

    [Fact]
    public async Task TokenEmitidoDespuesDeUnaRevocacionDelMismoMilisegundo_NoDevuelve401()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var revocador = Factory.Services.GetRequiredService<IRevocadorTokens>();

        var token = jwt.GenerarToken(UsuarioIdAislado, RolUsuario.Admin);

        // Instante de emisión tal como lo reconstruye OnTokenValidated: el iat truncado.
        var iat = long.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .First(c => c.Type == JwtRegisteredClaimNames.Iat).Value);
        var emitidoSegunIat = DateTimeOffset.FromUnixTimeMilliseconds(iat).UtcDateTime;

        // Revocación ANTERIOR a la emisión real del token: como el iat viene truncado al
        // piso del milisegundo, la emisión real ocurrió en o después de emitidoSegunIat,
        // así que un instante 1 tick posterior al piso sigue estando antes (o a lo sumo en)
        // el momento en que el token se firmó. Un token emitido después de la revocación
        // TIENE que seguir siendo válido.
        revocador.Revocar(UsuarioIdAislado, emitidoSegunIat.AddTicks(1));

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
