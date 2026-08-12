using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IAdjuntoDocumentoRepository
{
    Task<int> AgregarAsync(AdjuntoDocumento adjunto, byte[] contenido);
    Task<IReadOnlyList<AdjuntoDocumento>> ListarPorDocumentoAsync(int documentoId);
    Task<AdjuntoDocumento?> ObtenerPorIdAsync(int id);
    Task<byte[]?> ObtenerContenidoAsync(int adjuntoId);
    Task ActualizarAsync(AdjuntoDocumento adjunto);
}
