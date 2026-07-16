using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IGastoRepository
{
    /// <summary>Incluye Proveedor, Fuente, Rubro, LineaPoa y TODOS los pagos (activos y anulados).</summary>
    Task<Gasto?> ObtenerPorIdAsync(int id);

    /// <summary>Busca un gasto ACTIVO por proveedor + número de factura (conciliación del vínculo stock).</summary>
    Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura);

    /// <summary>Con includes. Ordena por Fecha desc, luego Id desc. Los filtros nulos no aplican.</summary>
    Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro);

    /// <summary>Inserta el gasto CON sus pagos (grafo completo — pago contado automático incluido).</summary>
    Task<int> AgregarAsync(Gasto gasto);

    /// <summary>Actualiza la cabecera. <paramref name="gasto"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Gasto gasto);

    Task<int> AgregarPagoAsync(PagoGasto pago);

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
}
