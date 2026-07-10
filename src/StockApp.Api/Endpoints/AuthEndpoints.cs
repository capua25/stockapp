using StockApp.Api.Auth;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;

namespace StockApp.Api.Endpoints;

public record LoginRequest(string? NombreUsuario, string? Contrasena);
public record LoginResponse(string Token);
public record PrimerArranqueEstadoResponse(bool RequiereCrearAdmin);
public record CrearAdminInicialRequest(string NombreUsuario, string Contrasena);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            IUsuarioRepository usuarios,
            IPasswordHasher hasher,
            IJwtTokenService jwtTokenService) =>
        {
            if (string.IsNullOrWhiteSpace(request.NombreUsuario)
                || string.IsNullOrWhiteSpace(request.Contrasena))
            {
                return Results.Problem(
                    title: "Usuario y contraseña son obligatorios.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var usuario = await usuarios.BuscarPorNombreAsync(request.NombreUsuario);

            // No se distingue "usuario inexistente" de "contraseña incorrecta" ni de
            // "usuario inactivo" en la respuesta (spec §2: no filtrar si el usuario existe).
            if (usuario is null
                || !usuario.Activo
                || !hasher.Verify(request.Contrasena, usuario.HashContrasena))
            {
                return Results.Problem(
                    title: "Usuario o contraseña inválidos.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var token = jwtTokenService.GenerarToken(usuario.Id, usuario.Rol);
            return Results.Ok(new LoginResponse(token));
        });

        group.MapGet("/primer-arranque", async (IPrimerArranqueService primerArranque) =>
        {
            var requiere = await primerArranque.RequiereCrearAdminAsync();
            return Results.Ok(new PrimerArranqueEstadoResponse(requiere));
        });

        group.MapPost("/primer-admin", async (
            CrearAdminInicialRequest request, IPrimerArranqueService primerArranque) =>
        {
            await primerArranque.CrearAdminInicialAsync(request.NombreUsuario, request.Contrasena);
            return Results.Created("/auth/primer-arranque", (object?)null);
        });

        return app;
    }
}
