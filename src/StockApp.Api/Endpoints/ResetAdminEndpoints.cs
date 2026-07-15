using Microsoft.AspNetCore.RateLimiting;
using StockApp.Application.Licenciamiento;

namespace StockApp.Api.Endpoints;

public record ResetDesafioResponse(string Desafio, string CodigoMaquina);
public record ResetAdminRequest(string? Token, string? NuevaContrasena);

public static class ResetAdminEndpoints
{
    public static IEndpointRouteBuilder MapResetAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/reset-admin");

        // Anónimo (pre-login): la protección es criptográfica (token firmado + nonce en memoria).
        group.MapPost("/desafio", (IAlmacenDesafiosReset desafios, EstadoLicencia estado) =>
            Results.Ok(new ResetDesafioResponse(desafios.GenerarNuevo(), estado.CodigoMaquina)))
            .RequireRateLimiting("licenciamiento");

        group.MapPost("", async (ResetAdminRequest request, ServicioResetAdmin servicio) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NuevaContrasena))
                return Results.Problem(
                    title: "El token y la nueva contraseña son obligatorios.",
                    statusCode: StatusCodes.Status400BadRequest);

            // ContrasenaValidator lanza ArgumentException (→ 400 vía DomainExceptionHandler)
            // si la contraseña es corta; el resto es flujo por enum.
            var resultado = await servicio.ResetearAsync(request.Token, request.NuevaContrasena);

            return resultado == ResultadoValidacionReset.Valido
                ? Results.Ok(new { ok = true })
                : Results.Problem(title: MotivoDe(resultado), statusCode: StatusCodes.Status400BadRequest);
        }).RequireRateLimiting("licenciamiento");

        return app;
    }

    private static string MotivoDe(ResultadoValidacionReset resultado) => resultado switch
    {
        ResultadoValidacionReset.FormatoInvalido => "El token no tiene un formato válido.",
        ResultadoValidacionReset.FirmaInvalido   => "La firma del token no es válida.",
        ResultadoValidacionReset.MaquinaDistinta => "El token fue emitido para otra máquina.",
        ResultadoValidacionReset.AccionInvalida  => "El token no es un token de reset de Admin.",
        ResultadoValidacionReset.DesafioInvalido => "El desafío no es válido o ya fue usado. Pedí uno nuevo.",
        ResultadoValidacionReset.DesafioExpirado => "El desafío expiró. Pedí uno nuevo.",
        ResultadoValidacionReset.FingerprintIlegible =>
            "No se pudo leer la identificación de esta máquina. Contactá a soporte.",
        _ => "Token de reset inválido.",
    };
}
