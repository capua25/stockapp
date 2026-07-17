using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class AdjuntosEndpoints
{
    public static IEndpointRouteBuilder MapAdjuntosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/finanzas/gastos/{id:int}/adjuntos", async (int id, IFormFile archivo, IAdjuntoService adjuntos) =>
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var dto = await adjuntos.AgregarAGastoAsync(id, archivo.FileName, ms.ToArray());
            return Results.Created((string?)null, dto);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.RegistrarGastos);

        app.MapPost("/finanzas/pagos/{id:int}/adjuntos", async (int id, IFormFile archivo, IAdjuntoService adjuntos) =>
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var dto = await adjuntos.AgregarAPagoAsync(id, archivo.FileName, ms.ToArray());
            return Results.Created((string?)null, dto);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.RegistrarPagos);

        app.MapGet("/finanzas/gastos/{id:int}/adjuntos", async (int id, IAdjuntoService adjuntos) =>
            Results.Ok(await adjuntos.ListarPorGastoAsync(id)))
            .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/pagos/{id:int}/adjuntos", async (int id, IAdjuntoService adjuntos) =>
            Results.Ok(await adjuntos.ListarPorPagoAsync(id)))
            .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/adjuntos/{id:int}/contenido", async (int id, IAdjuntoService adjuntos) =>
        {
            var contenido = await adjuntos.ObtenerContenidoAsync(id);
            return Results.File(contenido.Contenido, contenido.ContentType, contenido.NombreArchivo);
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        // No lleva un permiso concreto: RequireAuthorization(a, b) exige AMBOS (AND), no
        // "uno u otro" según el tipo de adjunto. La decisión fina (RegistrarGastos vs
        // RegistrarPagos según adjunto.EsDePago) la resuelve AdjuntoService.QuitarAsync,
        // que siempre corre y siempre valida — la doble barrera se cumple igual.
        app.MapDelete("/finanzas/adjuntos/{id:int}", async (int id, IAdjuntoService adjuntos) =>
        {
            await adjuntos.QuitarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization();

        return app;
    }
}
