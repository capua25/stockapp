using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Gastos y facturas del módulo Finanzas (spec parte 4-5, 10). El estado de la factura
/// NUNCA se persiste: lo calcula la entidad. La advertencia de sobregiro POA viaja en
/// el resultado (advierte, no bloquea). Fail-closed: cada método verifica autorización.
/// </summary>
public interface IGastoService
{
    /// <summary>
    /// Alta del gasto. Contado crea el pago automático por el total. Si vienen
    /// <paramref name="movimientoIds"/> (flujo "Asociar factura" de la entrada de stock),
    /// los valida (entradas sin gasto previo) y los vincula al gasto creado.
    /// </summary>
    Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null);

    Task<ResultadoGastoDto> ModificarAsync(Gasto gasto);

    /// <summary>Anulación (baja lógica). Exige que no haya pagos activos; desvincula sus movimientos.</summary>
    Task AnularAsync(int id);

    /// <summary>Lanza EntidadNoEncontradaException si no existe.</summary>
    Task<Gasto> ObtenerPorIdAsync(int id);

    /// <summary>Busca la factura ACTIVA de un proveedor (flujo "asociar a factura existente"). Null si no hay.</summary>
    Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura);

    Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro);

    /// <summary>Registra un pago del gasto. Devuelve el id del pago creado.</summary>
    Task<int> RegistrarPagoAsync(PagoGasto pago);

    Task AnularPagoAsync(int gastoId, int pagoId);

    /// <summary>Vincula movimientos de ENTRADA sin factura a un gasto existente.</summary>
    Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds);
}
