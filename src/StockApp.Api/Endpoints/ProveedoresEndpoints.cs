using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);
public record ModificarProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);
/// <summary>DTO de lectura de Proveedor (Fase 3a, D3). Reemplaza la entidad de dominio cruda.</summary>
public record ProveedorDto(
    int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas, bool Activo);

public static class ProveedoresEndpoints
{
    public static IEndpointRouteBuilder MapProveedoresEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/proveedores");

        group.MapGet("/", async (IProveedorService proveedores) =>
            Results.Ok((await proveedores.ListarTodosAsync()).Select(AProveedorDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

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
            // Sin Location: no existe GET /proveedores/{id} (el GET del recurso es la lista
            // completa o /activas) — emitir una ruta que no resuelve es peor que omitirla.
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

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
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IProveedorService proveedores) =>
        {
            await proveedores.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Lectura para Finanzas (bugfix 2026-08-15, mismo patrón que /finanzas/fuentes/activas,
        // /finanzas/rubros/activos y /finanzas/lineas-poa/activas): un gasto TIENE un proveedor,
        // así que un Operador con VerFinanzas pero sin GestionarTablasMaestras necesita poder
        // leerlos (filtrados a Activo=true) para armar el combo de "Gastos y facturas".
        group.MapGet("/activas", async (IProveedorService proveedores) =>
            Results.Ok((await proveedores.ListarActivasAsync()).Select(AProveedorDto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static ProveedorDto AProveedorDto(Proveedor p) =>
        new(p.Id, p.Nombre, p.Telefono, p.Email, p.Direccion, p.Notas, p.Activo);
}
