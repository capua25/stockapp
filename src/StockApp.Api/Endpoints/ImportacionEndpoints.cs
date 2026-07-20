using Microsoft.AspNetCore.Mvc;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class ImportacionEndpoints
{
    public static IEndpointRouteBuilder MapImportacionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/finanzas/importar/analizar", async (
            IFormFile gastos, IFormFile poa, [FromForm] int ejercicio, IAnalisisImportacionService analisis) =>
        {
            using var streamGastos = gastos.OpenReadStream();
            using var streamPoa = poa.OpenReadStream();
            var resultado = await analisis.AnalizarAsync(streamGastos, streamPoa, ejercicio);
            return Results.Ok(resultado);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.ImportarPlanillas);

        return app;
    }
}
