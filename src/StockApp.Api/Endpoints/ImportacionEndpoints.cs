using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class ImportacionEndpoints
{
    /// <summary>
    /// F5c §3: límite de tamaño de ARCHIVO para /analizar — mitigación real contra zip bomb
    /// (acota el input comprimido ANTES de que ZipArchive lo descomprima, F5a). Los .ods reales
    /// de este municipio pesan ~150KB (Gastos) y ~23KB (POA); 10MB es un techo generoso que
    /// igual corta cualquier archivo anormalmente grande antes de intentar parsearlo.
    /// </summary>
    private const long LimiteBytesArchivoOds = 10 * 1024 * 1024;

    /// <summary>
    /// F5c §3: límite de tamaño de BODY para /confirmar y /revertir — defensa en profundidad
    /// contra un payload JSON de abuso, NO mitigación de zip bomb (JSON plano, sin ZipArchive,
    /// no hay nada que descomprimir). Es un techo razonable, distinto en motivo del de arriba.
    /// </summary>
    private const long LimiteBytesBodyConfirmacion = 5 * 1024 * 1024;

    /// <summary>
    /// Filtro compartido de límite de tamaño de BODY (F5c §3), usado por /confirmar y /revertir
    /// — antes duplicado idéntico entre los dos endpoints.
    /// </summary>
    private static async ValueTask<object?> LimitarTamañoBody(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var limite = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limite is not null && !limite.IsReadOnly)
            limite.MaxRequestBodySize = LimiteBytesBodyConfirmacion;
        return await next(context);
    }

    public static IEndpointRouteBuilder MapImportacionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/finanzas/importar/analizar", async (
            IFormFile gastos, IFormFile poa, [FromForm] int ejercicio, IAnalisisImportacionService analisis) =>
        {
            if (gastos.Length > LimiteBytesArchivoOds || poa.Length > LimiteBytesArchivoOds)
            {
                throw new ArgumentException(
                    $"El archivo supera el límite permitido de {LimiteBytesArchivoOds / 1024 / 1024}MB.");
            }

            using var streamGastos = gastos.OpenReadStream();
            using var streamPoa = poa.OpenReadStream();
            var resultado = await analisis.AnalizarAsync(streamGastos, streamPoa, ejercicio);
            return Results.Ok(resultado);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.ImportarPlanillas);

        app.MapPost("/finanzas/importar/confirmar", async (
            ConfirmarImportacionDto dto, IConfirmacionImportacionService confirmacion) =>
        {
            var resultado = await confirmacion.ConfirmarAsync(dto);
            return Results.Ok(resultado);
        })
        .AddEndpointFilter(LimitarTamañoBody)
        .RequireAuthorization(Permisos.ImportarPlanillas);

        app.MapPost("/finanzas/importar/revertir/{id:guid}", async (
            Guid id, IConfirmacionImportacionService confirmacion) =>
        {
            var resultado = await confirmacion.RevertirAsync(id);
            return Results.Ok(resultado);
        })
        .AddEndpointFilter(LimitarTamañoBody)
        .RequireAuthorization(Permisos.ImportarPlanillas);

        return app;
    }
}
