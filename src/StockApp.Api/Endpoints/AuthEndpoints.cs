using StockApp.Api.Auth;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record LoginRequest(string? NombreUsuario, string? Contrasena);
public record UsuarioLoginResponse(int Id, string NombreUsuario, string? NombreCompleto, RolUsuario Rol);
public record LoginResponse(string Token, UsuarioLoginResponse Usuario);

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
            var usuarioResponse = new UsuarioLoginResponse(
                usuario.Id, usuario.NombreUsuario, usuario.NombreCompleto, usuario.Rol);
            return Results.Ok(new LoginResponse(token, usuarioResponse));
        });

        return app;
    }
}
