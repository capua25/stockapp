using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Args para el registro atómico de un movimiento de stock.
/// El service los compone; el repo los consume sin conocer reglas de negocio.
/// </summary>
public record RegistroAtomicoArgs(
    MovimientoStock Movimiento,   // entidad ya construida (sin Id)
    int ProductoId,
    TipoMovimiento Tipo,          // define el signo del delta
    decimal Cantidad,             // siempre positiva
    bool Forzar,                  // true → permite stock negativo (bypass del guard condicional)
    int UsuarioId,
    string DetalleAuditoria);     // payload listo para LogAuditoria.Detalle

/// <summary>Estado del intento de registro atómico.</summary>
public enum ResultadoRegistroEstado
{
    Ok,
    StockInsuficiente
}

/// <summary>
/// Resultado tipado del registro atómico. Ok → movimiento persistido; StockInsuficiente →
/// el UPDATE condicional afectó 0 filas (imposible lost-update, imposible negativo sin forzar).
/// StockResultante = stock tras el update (Ok) o stock actual sin tocar (StockInsuficiente).
/// </summary>
public record ResultadoRegistro(
    ResultadoRegistroEstado Estado,
    int MovimientoId,
    decimal StockResultante);

/// <summary>
/// Args para el recálculo atómico de stock.
/// </summary>
public record RecalculoAtomicoArgs(
    int ProductoId,
    decimal StockNuevo,
    int UsuarioId,
    string DetalleAuditoria);

/// <summary>
/// Contrato de persistencia para movimientos de stock.
/// Todos los métodos de escritura son atómicos: insert/update + auditoría en UN solo SaveChangesAsync.
/// </summary>
public interface IMovimientoStockRepository
{
    /// <summary>
    /// Lee el producto trackeado por el mismo contexto del repo (para validación previa en el service).
    /// </summary>
    Task<Producto?> ObtenerProductoAsync(int productoId);

    /// <summary>
    /// Suma neta de movimientos del producto y total de registros (para recálculo).
    /// Entrada: +Cantidad; Salida: -Cantidad. Retorna (Neto=0, Total=0) si no hay movimientos.
    /// </summary>
    Task<(decimal Neto, int Total)> SumarMovimientosAsync(int productoId);

    /// <summary>
    /// ATÓMICO: insert MovimientoStock + update Producto.StockActual + insert LogAuditoria
    /// en UN solo SaveChangesAsync. Retorna el Id generado del movimiento.
    /// </summary>
    Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args);

    /// <summary>
    /// ATÓMICO: update Producto.StockActual + insert LogAuditoria (Accion=18)
    /// en UN solo SaveChangesAsync.
    /// </summary>
    Task RecalcularAtomicoAsync(RecalculoAtomicoArgs args);

    /// <summary>
    /// Historial con filtros combinables, ordenado por Fecha descendente.
    /// Retorna lista vacía si no hay coincidencias.
    /// </summary>
    Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro);
}
