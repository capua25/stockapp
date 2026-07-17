namespace StockApp.Domain.Enums;

public enum AccionAuditada
{
    // ── Valores originales (0–6) — NO reordenar, están persistidos en BD ──────
    CambioPrecio      = 0,
    AltaProducto      = 1,
    BajaProducto      = 2,
    AltaUsuario       = 3,
    BajaUsuario       = 4,
    CambioRol         = 5,
    CambioContrasena  = 6,

    // ── Catálogo — Incremento 4 (append-only a partir de 7) ──────────────────
    AltaCategoria          = 7,
    BajaCategoria          = 8,
    ModificacionCategoria  = 9,
    AltaProveedor          = 10,
    BajaProveedor          = 11,
    ModificacionProveedor  = 12,
    AltaUnidadMedida       = 13,
    BajaUnidadMedida       = 14,   // reservado (baja lógica con Activo=false)
    ModificacionUnidadMedida = 15,
    ModificacionProducto   = 16,

    // ── Movimientos de Stock — Incremento 5 (append-only a partir de 17) ──────
    RegistroMovimiento     = 17,
    RecalculoStock         = 18,

    // ── Licenciamiento / Reset — Incremento 7 Fase B (append-only a partir de 19) ──
    ActivacionLicencia               = 19,
    IntentoActivacionLicenciaFallido = 20,
    ResetAdminFirmado                = 21,

    // ── Finanzas — Fase 1: maestros (append-only a partir de 22) ─────────────
    AltaFuenteFinanciamiento         = 22,
    ModificacionFuenteFinanciamiento = 23,
    BajaFuenteFinanciamiento         = 24,
    AltaRubroGasto                   = 25,
    ModificacionRubroGasto           = 26,
    BajaRubroGasto                   = 27,
    AltaLineaPoa                     = 28,
    ModificacionLineaPoa             = 29,
    BajaLineaPoa                     = 30,

    // ── Finanzas — Fase 2: gastos, pagos e ingresos (append-only a partir de 31) ──
    AltaGasto                   = 31,
    ModificacionGasto           = 32,
    AnulacionGasto              = 33,
    AltaPagoGasto               = 34,
    AnulacionPagoGasto          = 35,
    AltaIngresoCaja             = 36,
    ModificacionIngresoCaja     = 37,
    BajaIngresoCaja             = 38,
    AsociacionMovimientosAGasto = 39,

    // ── Finanzas — Fase 3: adjuntos (append-only a partir de 40) ─────────────
    AltaAdjunto = 40,
    BajaAdjunto = 41,
}
