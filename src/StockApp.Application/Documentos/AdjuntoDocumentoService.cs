using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Documentos;

public class AdjuntoDocumentoService : IAdjuntoDocumentoService
{
    private readonly IAdjuntoDocumentoRepository        _adjuntos;
    private readonly IDocumentoAdministrativoRepository _documentos;
    private readonly ICurrentSession                    _session;
    private readonly IAuthorizationService               _auth;
    private readonly IAuditLogger                        _audit;

    public AdjuntoDocumentoService(
        IAdjuntoDocumentoRepository adjuntos, IDocumentoAdministrativoRepository documentos,
        ICurrentSession session, IAuthorizationService auth, IAuditLogger audit)
    {
        _adjuntos   = adjuntos;
        _documentos = documentos;
        _session    = session;
        _auth       = auth;
        _audit      = audit;
    }

    public async Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var documento = await _documentos.ObtenerPorIdAsync(documentoId)
            ?? throw new EntidadNoEncontradaException($"Documento {documentoId} no encontrado.");

        // D11a: la regla corta en ambos sentidos, agregar Y quitar (ver QuitarAsync).
        if (!documento.EsActivo)
            throw new ReglaDeNegocioException(
                $"No se pueden agregar adjuntos a un documento en estado '{documento.Estado}': no está activo.");

        AdjuntoValidador.Validar(contenido, nombreArchivo);

        var adjunto = new AdjuntoDocumento
        {
            DocumentoAdministrativoId = documentoId,
            NombreArchivo = nombreArchivo,
            ContentType   = AdjuntoValidador.DetectarContentType(contenido)!,
            TamanoBytes   = contenido.LongLength,
            Activo        = true,
            FechaAltaUtc  = DateTime.UtcNow,
        };

        var id = await _adjuntos.AgregarAsync(adjunto, contenido);

        // D11d: adjuntar genera evento automático en el historial del documento dueño.
        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Se agregó el adjunto '{nombreArchivo}'.", esAutomatico: true);
        await _documentos.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaAdjuntoDocumento, "AdjuntoDocumento", id,
            $"Documento {documentoId} — {nombreArchivo}");

        return ADto(adjunto);
    }

    public async Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);
        return (await _adjuntos.ListarPorDocumentoAsync(documentoId)).Select(ADto).ToList();
    }

    public async Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        _auth.Verificar(_session, Permisos.GestionarDocumentos);

        var adjunto = await _adjuntos.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        // Baja lógica: un adjunto inactivo no debe seguir siendo descargable por id, mismo
        // criterio que AdjuntoService.ObtenerContenidoAsync (Finanzas).
        if (!adjunto.Activo)
            throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        var contenido = await _adjuntos.ObtenerContenidoAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el contenido del adjunto {adjuntoId}.");

        return new AdjuntoDocumentoContenidoDto(adjunto.NombreArchivo, adjunto.ContentType, contenido);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        _auth.Verificar(_session, Permisos.AdministrarDocumentos);

        var adjunto = await _adjuntos.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        var documento = await _documentos.ObtenerPorIdAsync(adjunto.DocumentoAdministrativoId)
            ?? throw new EntidadNoEncontradaException($"Documento {adjunto.DocumentoAdministrativoId} no encontrado.");

        // D11a corregido: la regla de "solo sobre documento activo" corta igual al agregar
        // y al quitar, no únicamente al agregar.
        if (!documento.EsActivo)
            throw new ReglaDeNegocioException(
                $"No se pueden quitar adjuntos de un documento en estado '{documento.Estado}': no está activo.");

        adjunto.Activo = false;
        await _adjuntos.ActualizarAsync(adjunto);

        documento.AgregarEvento(
            _session.UsuarioActual!.Id, $"Se quitó el adjunto '{adjunto.NombreArchivo}'.", esAutomatico: true);
        await _documentos.ActualizarAsync(documento);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.BajaAdjuntoDocumento, "AdjuntoDocumento", adjuntoId,
            $"{adjunto.NombreArchivo}");
    }

    private static AdjuntoDocumentoDto ADto(AdjuntoDocumento a) => new(
        a.Id, a.DocumentoAdministrativoId, a.NombreArchivo, a.ContentType, a.TamanoBytes, a.FechaAltaUtc);
}
