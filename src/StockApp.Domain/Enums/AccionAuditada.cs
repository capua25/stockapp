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
}
