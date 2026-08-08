using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class LogAuditoria
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; }
    public AccionAuditada Accion { get; set; }
    public string Entidad { get; set; } = string.Empty;   // ej: "Producto", "Usuario"
    public int EntidadId { get; set; }
    public string Detalle { get; set; } = string.Empty;   // ej: "PrecioVenta 100,00 → 120,00"

    /// <summary>
    /// Guid del lote de un proceso batch (hoy: el importador de planillas F5c) que generó este
    /// log — nullable porque el resto de la auditoría del sistema (stock, precios, usuarios,
    /// licencias) no participa de ningún lote y deja esta columna en null para siempre. Columna
    /// tipada con índice NO único (F5c, post-review de Task 6): reemplaza la codificación previa
    /// como texto embebido en <see cref="Detalle"/>, que forzaba un seq scan sobre una tabla
    /// compartida por todo el sistema para ubicar el log de un lote.
    ///
    /// fix/integridad-referencial: se evaluó y se descartó a propósito (a) renombrarlo a
    /// IdImportacion para "unificar" con Gasto/IngresoCaja/LineaPoa/PagoGasto.IdImportacion, y
    /// (b) convertirlo en FK hacia LoteImportacion. Ambos acoplarían esta bitácora GENÉRICA
    /// (AccionAuditada tiene 51 valores, de los cuales solo 2 son de importación) al módulo de
    /// importación — un futuro proceso batch no relacionado (anulación masiva, recálculo) no
    /// podría reusar este mismo campo si quedara atado por nombre o por FK a LotesImportacion.
    /// </summary>
    public Guid? IdLote { get; set; }
}
