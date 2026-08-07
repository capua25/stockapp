using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record CrearUsuarioRequest(string NombreUsuario, string? NombreCompleto, string ContrasenaPlan, RolUsuario Rol);
// NuevoRol es nullable a propósito: RolUsuario.Admin = 0, así que un body sin el campo
// (ej. "{}") dejaba a System.Text.Json poner el default del tipo — 0 = Admin — y
// promovía al usuario en silencio con 200 OK. Con el campo nullable, la ausencia
// deserializa a null y el handler la rechaza explícitamente con 400.
public record CambiarRolRequest(RolUsuario? NuevoRol);
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
            if (request.NuevoRol is null)
                throw new ArgumentException("El campo 'nuevoRol' es obligatorio.");

            await usuarios.CambiarRolAsync(id, request.NuevoRol.Value);
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
