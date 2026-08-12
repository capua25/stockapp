using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record EventoDocumentoDto(
    int Id, int UsuarioId, DateTime Fecha,
    EstadoDocumento? EstadoAnterior, EstadoDocumento? EstadoNuevo,
    string Texto, bool EsAutomatico);

public record DocumentoDto(
    int Id, string Numero, int Anio, TipoDocumento Tipo,
    DateTime FechaEmision, string Descripcion, EstadoDocumento Estado,
    int RegistradoPorUsuarioId, string? RegistradoPorNombre,
    DateTime FechaRegistro, DateTime? FechaCierre,
    bool EsActivo, bool EsCerrado,
    List<EventoDocumentoDto> Eventos);

public record CrearDocumentoRequest(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
public record EditarDocumentoRequest(string Numero, int Anio, TipoDocumento Tipo, DateTime FechaEmision, string Descripcion);
public record DocumentoCreadoResponse(int Id);
public record AgregarNotaDocumentoRequest(string Texto);
public record MotivoRequest(string Motivo);

public static class DocumentosEndpoints
{
    public static IEndpointRouteBuilder MapDocumentosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/documentos");

        group.MapGet("/activos", async (
            TipoDocumento? tipo, int? anio, string? texto, IDocumentoAdministrativoService documentos) =>
        {
            var filtro = new FiltroDocumentos(tipo, anio, texto, null);
            return Results.Ok((await documentos.ListarActivosAsync(filtro)).Select(ADto));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        // D9: anio es OBLIGATORIO acá — el binding lo deja pasar como null (int? de Minimal API),
        // ListarHistorialAsync es quien lo rechaza con ArgumentException -> 400 (D9/Application).
        group.MapGet("/historial", async (
            TipoDocumento? tipo, int? anio, string? texto, EstadoDocumento? estado,
            IDocumentoAdministrativoService documentos) =>
        {
            var filtro = new FiltroDocumentos(tipo, anio, texto, estado);
            return Results.Ok((await documentos.ListarHistorialAsync(filtro)).Select(ADto));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapGet("/{id:int}", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            var documento = await documentos.ObtenerPorIdAsync(id);
            return documento is null ? Results.NotFound() : Results.Ok(ADto(documento));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/", async (CrearDocumentoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            var documento = new DocumentoAdministrativo
            {
                Numero = request.Numero,
                Anio = request.Anio,
                Tipo = request.Tipo,
                FechaEmision = request.FechaEmision,
                Descripcion = request.Descripcion,
            };
            var id = await documentos.RegistrarAsync(documento);
            return Results.Created((string?)null, new DocumentoCreadoResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPut("/{id:int}", async (int id, EditarDocumentoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.EditarAsync(id, new DatosEdicionDocumento(
                request.Numero, request.Anio, request.Tipo, request.FechaEmision, request.Descripcion));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/iniciar", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.IniciarProcesoAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/volver-a-pendiente", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.VolverAPendienteAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/finalizar", async (int id, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.FinalizarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/notas", async (int id, AgregarNotaDocumentoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.AgregarNotaAsync(id, request.Texto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapPost("/{id:int}/anular", async (int id, MotivoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.AnularAsync(id, request.Motivo);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarDocumentos);

        group.MapPost("/{id:int}/reabrir", async (int id, MotivoRequest request, IDocumentoAdministrativoService documentos) =>
        {
            await documentos.ReabrirAsync(id, request.Motivo);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarDocumentos);

        group.MapPost("/{id:int}/adjuntos", async (int id, IFormFile archivo, IAdjuntoDocumentoService adjuntos) =>
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var dto = await adjuntos.AgregarAsync(id, archivo.FileName, ms.ToArray());
            return Results.Created((string?)null, dto);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapGet("/{id:int}/adjuntos", async (int id, IAdjuntoDocumentoService adjuntos) =>
            Results.Ok(await adjuntos.ListarPorDocumentoAsync(id)))
            .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapGet("/adjuntos/{adjuntoId:int}/contenido", async (int adjuntoId, IAdjuntoDocumentoService adjuntos) =>
        {
            var contenido = await adjuntos.ObtenerContenidoAsync(adjuntoId);
            return Results.File(contenido.Contenido, contenido.ContentType, contenido.NombreArchivo);
        })
        .RequireAuthorization(Permisos.GestionarDocumentos);

        group.MapDelete("/adjuntos/{adjuntoId:int}", async (int adjuntoId, IAdjuntoDocumentoService adjuntos) =>
        {
            await adjuntos.QuitarAsync(adjuntoId);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarDocumentos);

        return app;
    }

    private static DocumentoDto ADto(DocumentoAdministrativo d) => new(
        d.Id, d.Numero, d.Anio, d.Tipo,
        d.FechaEmision, d.Descripcion, d.Estado,
        d.RegistradoPorUsuarioId, d.RegistradoPor?.NombreUsuario,
        d.FechaRegistro, d.FechaCierre,
        d.EsActivo, d.EsCerrado,
        d.Eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Id)
            .Select(e => new EventoDocumentoDto(
                e.Id, e.UsuarioId, e.Fecha, e.EstadoAnterior, e.EstadoNuevo, e.Texto, e.EsAutomatico))
            .ToList());
}
