using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IGastoRepository
{
    /// <summary>Incluye Proveedor, Fuente, Rubro, LineaPoa y TODOS los pagos (activos y anulados).</summary>
    Task<Gasto?> ObtenerPorIdAsync(int id);

    /// <summary>
    /// Busca un gasto ACTIVO por proveedor + número de factura + número de orden. Espeja
    /// EXACTAMENTE la clave del índice único parcial de la base (AppDbContext.cs, migración
    /// AmpliaIndiceFacturaConNumeroOrden — F5c): dos usos comparten este método, la validación de
    /// unicidad de <c>GastoService.ValidarFacturaUnicaAsync</c> (necesita el match exacto que
    /// dispara el índice) y la conciliación del vínculo stock (spec §5.1, "¿ya existe esta
    /// factura+orden para este proveedor?"). <paramref name="numeroOrden"/> puede ser null —
    /// el índice usa NULLS NOT DISTINCT, así que dos gastos sin orden SÍ colisionan entre sí.
    /// </summary>
    Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden);

    /// <summary>Con includes. Ordena por Fecha desc, luego Id desc. Los filtros nulos no aplican.</summary>
    Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro);

    /// <summary>Inserta el gasto CON sus pagos (grafo completo — pago contado automático incluido).</summary>
    Task<int> AgregarAsync(Gasto gasto);

    /// <summary>Actualiza la cabecera. <paramref name="gasto"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Gasto gasto);

    /// <summary>
    /// Registra el pago dentro de una transacción que bloquea (FOR UPDATE) la fila del
    /// gasto y re-verifica el saldo pendiente contra los pagos activos YA committeados
    /// antes de insertar — cierra la ventana de sobrepago por pagos concurrentes que el
    /// check-then-insert en memoria (validar en el service y recién ahí insertar) dejaba
    /// abierta. Lanza <see cref="StockApp.Domain.Exceptions.EntidadNoEncontradaException"/>
    /// si el gasto no existe y <see cref="StockApp.Domain.Exceptions.ReglaDeNegocioException"/>
    /// si está anulado o si el pago supera el saldo pendiente re-verificado.
    /// </summary>
    Task<int> RegistrarPagoAtomicoAsync(PagoGasto pago);

    /// <summary><paramref name="pago"/> debe ser una instancia tracked (hija de ObtenerPorIdAsync).</summary>
    Task ActualizarPagoAsync(PagoGasto pago);

    /// <summary>Suma MontoTotal de los gastos ACTIVOS de esa línea POA + fuente (para la advertencia de sobregiro).</summary>
    Task<decimal> TotalGastadoLineaFuenteAsync(int lineaPoaId, int fuenteFinanciamientoId, int? excluyendoGastoId = null);

    /// <summary>Trae los movimientos de stock por id (para validar el vínculo antes de asignar).</summary>
    Task<IReadOnlyList<MovimientoStock>> ObtenerMovimientosAsync(IReadOnlyList<int> movimientoIds);

    /// <summary>Setea GastoId en los movimientos indicados.</summary>
    Task AsignarGastoAMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds);

    /// <summary>Pone GastoId = null en todos los movimientos del gasto (al anularlo).</summary>
    Task DesvincularMovimientosAsync(int gastoId);

    /// <summary>
    /// Pagos ACTIVOS de gastos ACTIVOS con Fecha (del PAGO, no de la factura) en
    /// [desdeUtc, hastaUtc]. Include Gasto→Proveedor/RubroGasto/FuenteFinanciamiento
    /// (libro caja: cada fila de egreso necesita esos nombres). Ordena por Fecha, luego Id.
    /// </summary>
    Task<IReadOnlyList<PagoGasto>> ListarPagosActivosPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc);

    /// <summary>Suma de Monto de los pagos ACTIVOS de gastos ACTIVOS con Fecha &lt; fechaUtc (saldo inicial).</summary>
    Task<decimal> TotalPagosActivosAntesDeAsync(DateTime fechaUtc);

    /// <summary>Gastos ACTIVOS con Includes (Proveedor/Fuente/Rubro/LineaPoa/Pagos) para el calendario de pagos.</summary>
    Task<IReadOnlyList<Gasto>> ListarActivosConSaldoAsync();

    /// <summary>
    /// Suma MontoTotal de gastos ACTIVOS agrupada por LineaPoaId, restringida a líneas
    /// del ejercicio indicado (control POA).
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> TotalGastadoPorLineaAsync(int ejercicio);
}
