namespace StockApp.Application.Documentos;

/// <summary>
/// Adjuntos de documentos administrativos (D10/D11). Entidad y tabla propias — no reusa
/// Adjunto de Finanzas — pero SÍ reusa AdjuntoValidador tal cual (D10). Sin método
/// Modificar: un adjunto no se edita, se quita y se sube otro, mismo criterio que
/// IAdjuntoService de Finanzas (YAGNI, spec F3 decisión 8).
/// </summary>
public interface IAdjuntoDocumentoService
{
    /// <summary>Rechaza con ReglaDeNegocioException si el documento no está EsActivo (D11a).</summary>
    Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido);

    Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId);

    Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId);

    /// <summary>Baja lógica (D11c). Exige Permisos.AdministrarDocumentos (D11b) y también
    /// rechaza si el documento dueño no está EsActivo (D11a — corta en ambos sentidos).</summary>
    Task QuitarAsync(int adjuntoId);
}
