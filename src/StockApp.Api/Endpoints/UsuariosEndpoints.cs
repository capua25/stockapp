using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record CrearUsuarioRequest(string NombreUsuario, string? NombreCompleto, string ContrasenaPlan, RolUsuario Rol);
public record CambiarRolRequest(RolUsuario NuevoRol);
public record CambiarContrasenaRequest(string NuevaContrasena, string? ContrasenaActual);
public record UsuarioCreadoResponse(int Id);

public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/usuarios").RequireAuthorization(Permisos.GestionarUsuarios);

        group.MapGet("/", async (IUsuarioService usuarios) =>
            Results.Ok(await usuarios.ListarAsync()));

        group.MapPost("/", async (CrearUsuarioRequest request, IUsuarioService usuarios) =>
        {
            var id = await usuarios.AltaUsuarioAsync(
                request.NombreUsuario, request.NombreCompleto, request.ContrasenaPlan, request.Rol);
            // Sin Location: no existe GET /usuarios/{id} (el único GET del recurso es la lista
            // completa) — emitir una ruta que no resuelve es peor que omitirla.
            return Results.Created((string?)null, new UsuarioCreadoResponse(id));
        });

        group.MapDelete("/{id:int}", async (int id, IUsuarioService usuarios) =>
        {
            await usuarios.BajaLogicaAsync(id);
            return Results.Ok();
        });

        group.MapPut("/{id:int}/rol", async (int id, CambiarRolRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.CambiarRolAsync(id, request.NuevoRol);
            return Results.Ok();
        });

        group.MapPut("/{id:int}/contrasena", async (int id, CambiarContrasenaRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.CambiarContrasenaAsync(id, request.NuevaContrasena, request.ContrasenaActual);
            return Results.Ok();
        });

        return app;
    }
}
