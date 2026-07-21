using StockApp.Application.Finanzas;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Escritura transaccional del importador de planillas (F5c). Abre UNA transacción por
/// operación (confirmar/revertir) — es el único lugar de todo el flujo de importación que toca
/// EF/Npgsql directamente; Application no referencia esas dependencias (mismo criterio ya
/// documentado en GastoRepository.RegistrarPagoAtomicoAsync, GastoRepository.cs:113-121).
/// </summary>
public interface IImportacionRepository
{
    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId);
    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId);
}
