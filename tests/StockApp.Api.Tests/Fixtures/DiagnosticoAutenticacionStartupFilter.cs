using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Application.Auth;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Instrumentación del 401 intermitente (Parte 2 del fix de flakiness, ver
/// .superpowers/sdd/flakiness-fix.md, Problema A de flakiness-investigacion.md). No se
/// determinó la causa raíz ni se pudo reproducir en 17 corridas completas -- en vez de
/// parchear algo sin evidencia (explícitamente rechazado: un "reset defensivo" de
/// IRevocadorTokens no se implementó porque, si el flake dejara de aparecer, no
/// sabríamos si se arregló o si simplemente no volvió a darse), esto deja evidencia
/// lista para la PRÓXIMA vez que el 401 aparezca.
///
/// Diseño:
/// - <see cref="IStartupFilter"/> es el punto de menor invasión: envuelve el pipeline
///   HTTP del host de test SIN tocar Program.cs (código de producción) ni ninguno de
///   los 278 tests uno por uno. Se registra UNA vez en ApiFactory.ConfigureWebHost y
///   cubre automáticamente todos los requests de la collection "Api". Se prefirió esto
///   a un DelegatingHandler del lado del cliente (insertado vía
///   WebApplicationFactory.CreateDefaultClient) porque esa familia de métodos NO es
///   virtual en la versión de Microsoft.AspNetCore.Mvc.Testing de este repo (10.*) --
///   se probó primero y no compiló.
/// - NO cambia el comportamiento de ningún test: solo OBSERVA el `HttpContext.Response`
///   ya calculado por el resto del pipeline (incluida la autenticación JWT), nunca lo
///   modifica.
/// - NO ensucia corridas normales: solo escribe a Console cuando la respuesta es 401.
///   Los MUCHOS tests que assertan 401 a propósito (credenciales inválidas, sin token,
///   rol equivocado, etc.) y HOY PASAN disparan esta escritura igual -- pero dotnet
///   test / xUnit solo muestran la salida de Console capturada para tests que FALLAN
///   (verificado empíricamente, ver reporte), así que en la práctica no aparece en una
///   corrida verde.
/// - NO loguea el token crudo firmado -- solo los claims decodificados (sub/rol/iat),
///   que no son secretos y no permiten reconstruir la firma.
/// </summary>
internal sealed class DiagnosticoAutenticacionStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Registrado ANTES de `next(app)`, así que este middleware queda como el
            // MÁS EXTERNO del pipeline -- su `await siguiente()` envuelve TODO lo demás
            // (incluida la autenticación JWT de Program.cs), y cuando retoma el control
            // context.Response.StatusCode ya refleja el resultado final del request.
            app.Use(async (context, siguiente) =>
            {
                var enviadoEnUtc = DateTime.UtcNow;
                await siguiente();

                if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                    Console.WriteLine(FormatearDiagnostico(context, enviadoEnUtc));
            });

            next(app);
        };
    }

    private static string FormatearDiagnostico(HttpContext context, DateTime enviadoEnUtc)
    {
        var recibidoEnUtc = DateTime.UtcNow;
        var sb = new StringBuilder();
        sb.AppendLine("[DIAGNOSTICO-401] Respuesta 401 Unauthorized -- ver Parte 2 de .superpowers/sdd/flakiness-fix.md");
        sb.AppendLine($"  Request:  {context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
        sb.AppendLine($"  Enviado (UTC, según el proceso de test):   {enviadoEnUtc:O}");
        sb.AppendLine($"  Recibido (UTC, según el proceso de test):  {recibidoEnUtc:O}");

        var header = context.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..]
            : null;

        if (string.IsNullOrEmpty(token))
        {
            sb.AppendLine("  Token: (el request no llevaba header Authorization: Bearer)");
            return sb.ToString();
        }

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            var usuarioIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == StockAppClaimTypes.UsuarioId)?.Value;
            var rolClaim = jwt.Claims.FirstOrDefault(c => c.Type == StockAppClaimTypes.Rol)?.Value;
            var iatClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Iat)?.Value;

            DateTime? emitidoEnUtc = null;
            if (iatClaim is not null && long.TryParse(iatClaim, out var iatEpochMs))
                emitidoEnUtc = DateTimeOffset.FromUnixTimeMilliseconds(iatEpochMs).UtcDateTime;

            sb.AppendLine("  Claims del token usado (decodificados -- NUNCA el token crudo firmado):");
            sb.AppendLine($"    usuarioId = {usuarioIdClaim ?? "(ausente)"}");
            sb.AppendLine($"    rol       = {rolClaim ?? "(ausente)"}");
            sb.AppendLine($"    iat       = {iatClaim ?? "(ausente)"}" +
                (emitidoEnUtc is not null ? $" ({emitidoEnUtc:O} UTC)" : " (no parseable)"));
            sb.AppendLine($"    exp       = {jwt.ValidTo:O} UTC" +
                (jwt.ValidTo < recibidoEnUtc ? " -- VENCIDO al momento de la respuesta" : ""));

            if (usuarioIdClaim is not null && int.TryParse(usuarioIdClaim, out var usuarioId))
            {
                var revocador = context.RequestServices.GetRequiredService<IRevocadorTokens>();
                var estado = revocador.ObtenerEstadoDiagnostico();

                if (estado.TryGetValue(usuarioId, out var minimoAceptado))
                {
                    var explicacion = emitidoEnUtc is not null && emitidoEnUtc < minimoAceptado
                        ? " -- el iat del token es ANTERIOR a esa marca: EsValido() lo hubiera rechazado por revocación"
                        : " -- el iat del token no es anterior a esa marca (no explica un 401 por revocación)";
                    sb.AppendLine($"  IRevocadorTokens: usuarioId {usuarioId} tiene mínimo aceptado = {minimoAceptado:O} UTC{explicacion}");
                }
                else
                {
                    sb.AppendLine($"  IRevocadorTokens: usuarioId {usuarioId} no tiene revocación registrada (el 401 no viene de acá).");
                }

                sb.AppendLine($"  IRevocadorTokens: estado completo ({estado.Count} usuario(s) con revocación activa): " +
                    (estado.Count == 0
                        ? "(vacío)"
                        : string.Join(", ", estado.Select(kv => $"usuarioId={kv.Key}@{kv.Value:O}"))));
            }
            else
            {
                sb.AppendLine("  IRevocadorTokens: no se pudo consultar (falta o no parsea el claim usuarioId).");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Token: no se pudo decodificar ({ex.GetType().Name}: {ex.Message}) -- probablemente inválido/malformado.");
        }

        return sb.ToString();
    }
}
