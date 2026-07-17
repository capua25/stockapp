using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IAdjuntoRepository
{
    /// <summary>Metadatos únicamente (sin bytes). Null si no existe.</summary>
    Task<Adjunto?> ObtenerPorIdAsync(int id);

    /// <summary>Metadatos de adjuntos ACTIVOS del gasto, ordenados por FechaAltaUtc desc.</summary>
    Task<IReadOnlyList<Adjunto>> ListarPorGastoAsync(int gastoId);

    /// <summary>Metadatos de adjuntos ACTIVOS del pago, ordenados por FechaAltaUtc desc.</summary>
    Task<IReadOnlyList<Adjunto>> ListarPorPagoAsync(int pagoGastoId);

    /// <summary>Inserta metadatos + contenido en una sola transacción. Devuelve el Id generado.</summary>
    Task<int> AgregarAsync(Adjunto adjunto, byte[] contenido);

    /// <summary>Bytes del adjunto (tabla separada). Null si no existe el adjunto.</summary>
    Task<byte[]?> ObtenerContenidoAsync(int adjuntoId);

    /// <summary><paramref name="adjunto"/> debe ser instancia tracked de ObtenerPorIdAsync. Usado solo para baja lógica.</summary>
    Task ActualizarAsync(Adjunto adjunto);
}
