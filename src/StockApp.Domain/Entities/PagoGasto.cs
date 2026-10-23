using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Pago (total o parcial) de un gasto. Hija del agregado Gasto, con baja lógica PROPIA
/// (a diferencia de AsignacionPresupuestal): anular un pago conserva la historia y
/// recalcula el estado de la factura. Contado ⇒ se crea un pago automático por el
/// total en la fecha del gasto.
/// </summary>
public class PagoGasto
{
    public int Id { get; set; }
    public int GastoId { get; set; }

    /// <summary>
    /// Navegación inversa (F4, vistas calculadas): permite Include(p => p.Gasto) desde
    /// consultas que arrancan en PagosGasto (ej. libro caja, que necesita Proveedor/Rubro/
    /// Fuente del gasto dueño de cada pago). Mismo FK GastoId que ya existía — sin
    /// migración nueva, solo se reconfigura la relación en AppDbContext.OnModelCreating.
    /// La caja y el literal que pagan viven en el propio pago (<see cref="Caja"/> y
    /// <see cref="FuenteFinanciamientoId"/>), no en el gasto.
    /// </summary>
    public Gasto? Gasto { get; set; }

    public DateTime Fecha { get; set; }        // UTC — el saldo de caja impacta ACÁ
    public decimal Monto { get; set; }         // precisión 18,4
    public string? Nota { get; set; }
    public bool Activo { get; set; } = true;   // false = pago anulado

    /// <summary>Caja de la que sale el pago. Default Local (todo lo existente migra a Local).</summary>
    public Caja Caja { get; set; } = Caja.Local;

    /// <summary>Literal (fuente de financiamiento) que paga este pago.</summary>
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }

    /// <summary>
    /// True si el pago lo creó el propio sistema (Contado ⇒ pago automático por el total,
    /// spec §4), false si lo tipeó un operador a mano (ABM manual / RegistrarPagoAsync).
    /// Identifica el pago automático de forma robusta para la anulación en cascada
    /// (Gasto.PagosAutomaticosADarDeBajaEnAnulacion) — comparar contra el texto de
    /// <see cref="Nota"/> ("Pago contado (automático)") sería frágil: cualquier cambio de
    /// wording futuro rompería la detección en silencio. Default false: todo pago existente
    /// antes de esta propiedad se trata como manual salvo que la migración lo corrija.
    /// </summary>
    public bool EsAutomatico { get; private set; }

    /// <summary>
    /// Guid del lote de /confirmar que creó este pago (F5c Task 8, re-review CRITICAL/IMPORTANT
    /// 2). Null para todo pago cargado a mano (ABM manual, incluidos los que un operador registra
    /// después sobre un gasto importado) — mismo patrón que Gasto.IdImportacion/
    /// IngresoCaja.IdImportacion/LineaPoa.IdImportacion. Permite a /revertir/{id} distinguir el
    /// pago automático de contado que el propio importador creó (que SÍ se da de baja en la
    /// reversa) de un pago manual real (que NUNCA se toca: la reversa se bloquea si lo encuentra).
    /// </summary>
    // Esquema del importador ODS (eliminado 2026-10-21): inutilizado, se conserva para no generar drops. No borrar sin decisión.
    public Guid? IdImportacion { get; set; }

    /// <summary>
    /// Único punto de construcción de un pago automático de contado (spec §4). Existe porque
    /// hubo un origen real (ImportacionRepository.ConfirmarAsync) que armaba el PagoGasto a mano
    /// y se olvidó de EsAutomatico=true — el guard de anulación en cascada
    /// (Gasto.PagosAutomaticosADarDeBajaEnAnulacion) lo trataba entonces como un pago MANUAL y
    /// bloqueaba la anulación individual del gasto. Centralizar la construcción acá hace que
    /// EsAutomatico=true sea imposible de olvidar en un cuarto origen futuro.
    /// </summary>
    public static PagoGasto Automatico(DateTime fecha, decimal monto, string nota,
        Caja caja, int fuenteFinanciamientoId, Guid? idImportacion = null)
        => new()
        {
            Fecha = fecha, Monto = monto, Nota = nota, Caja = caja,
            FuenteFinanciamientoId = fuenteFinanciamientoId, IdImportacion = idImportacion, EsAutomatico = true,
        };
}
