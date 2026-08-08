namespace StockApp.Domain.Entities;

/// <summary>
/// Lote de una corrida del importador de planillas (spec F5c/F5d), como entidad real en vez de
/// cinco columnas Guid sueltas sin FK (fix/integridad-referencial). Antes, la fuente de verdad
/// era LogsAuditoria (Accion == ImportacionPlanillas/ReversionImportacion) — una bitácora
/// genérica sin integridad referencial hacia Gastos/IngresosCaja/LineasPoa/PagosGasto.IdImportacion,
/// que quedaban con FKs "fantasma" (nullable, sin constraint) apuntando a nada verificable.
///
/// Id lo genera la APP (Guid.NewGuid()), no la base — ConfirmarAsync necesita el valor ANTES del
/// único SaveChangesAsync de la corrida para poder estampar el mismo Id en todos los hijos
/// (Gasto/IngresoCaja/LineaPoa/PagoGasto) del mismo grafo. Ver AppDbContext.OnModelCreating,
/// ValueGeneratedNever().
/// </summary>
public class LoteImportacion
{
    public Guid Id { get; set; }

    /// <summary>UTC — fecha de la confirmación (ConfirmarAsync).</summary>
    public DateTime Fecha { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    /// <summary>Antes viajaba disfrazado en LogAuditoria.EntidadId (F5c/F5d).</summary>
    public int Ejercicio { get; set; }

    public DateTime? RevertidaEn { get; private set; }
    public int? RevertidaPorUsuarioId { get; private set; }

    /// <summary>
    /// Único punto de mutación del estado "revertida" — mismo criterio que
    /// PagoGasto.Automatico (ver PagoGastoTests): RevertidaEn/RevertidaPorUsuarioId solo pueden
    /// setearse JUNTOS. Un lote con uno seteado y el otro null quedaría en un estado ambiguo que
    /// ListarHistorialAsync/RevertirAsync no sabrían interpretar (¿está revertido o no?).
    /// </summary>
    public void MarcarRevertida(DateTime fecha, int usuarioId)
    {
        RevertidaEn = fecha;
        RevertidaPorUsuarioId = usuarioId;
    }
}
