namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de Adjunto SIN Modificar (YAGNI, spec F3 decisión 8): un adjunto no se edita, se
/// quita y se sube otro. Doble barrera de permisos reusados (RegistrarGastos/RegistrarPagos/
/// VerFinanzas) — sin permisos nuevos.
/// </summary>
public interface IAdjuntoService
{
    Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido);
    Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido);
    Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId);
    Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId);
    Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId);
    Task QuitarAsync(int adjuntoId);
}
