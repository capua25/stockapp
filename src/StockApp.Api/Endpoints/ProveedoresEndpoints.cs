using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);
public record ModificarProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);

public static class ProveedoresEndpoints
{
    public static IEndpointRouteBuilder MapProveedoresEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/proveedores").RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapGet("/", async (IProveedorService proveedores) =>
            Results.Ok(await proveedores.ListarTodosAsync()));

        group.MapPost("/", async (CrearProveedorRequest request, IProveedorService proveedores) =>
        {
            var proveedor = new Proveedor
            {
                Nombre = request.Nombre,
                Telefono = request.Telefono,
                Email = request.Email,
                Direccion = request.Direccion,
                Notas = request.Notas,
            };
            var id = await proveedores.AltaAsync(proveedor);
            return Results.Created($"/proveedores/{id}", new { id });
        });

        group.MapPut("/{id:int}", async (int id, ModificarProveedorRequest request, IProveedorService proveedores) =>
        {
            var proveedor = new Proveedor
            {
                Id = id,
                Nombre = request.Nombre,
                Telefono = request.Telefono,
                Email = request.Email,
                Direccion = request.Direccion,
                Notas = request.Notas,
            };
            await proveedores.ModificarAsync(proveedor);
            return Results.Ok();
        });

        group.MapDelete("/{id:int}", async (int id, IProveedorService proveedores) =>
        {
            await proveedores.BajaLogicaAsync(id);
            return Results.Ok();
        });

        return app;
    }
}
