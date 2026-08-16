using System.Collections.Generic;
using StockApp.Application.Authorization;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Catálogo literal de los permisos configurables que muestra el panel de permisos de
/// UsuariosAdminView (Tasks 3/4/6, spec 2026-08-15). Reemplaza las 12 propiedades bool +
/// checkboxes hardcodeados a mano en PanelPermisosViewModel/UsuariosAdminView.axaml.
///
/// Vive en Presentation (no en Application) porque Etiqueta/Grupo son copy de una sola
/// pantalla de un solo cliente -- Application se comparte con StockApp.Api, que no tiene
/// ningún concepto de "pantalla".
///
/// Lista literal explícita, sin reflection ni atributos -- misma convención documentada en
/// Permisos.cs (Todos/PermisosConfigurables como listas a mano, nunca derivadas por
/// convención de nombres). La reflexión por convención de nombres es exactamente el
/// mecanismo que falló acá: GestionarDocumentos se agregó a Permisos.Todos/
/// PermisosConfigurables y nadie agregó su checkbox correspondiente en el panel, así que se
/// lo borraba en silencio a cualquier Operador que se editara (bug real, 2026-08-14).
///
/// Etiquetas y grupos: copiados textualmente del AXAML original de UsuariosAdminView (no son
/// texto nuevo). Cuatro permisos que antes vivían detrás de un checkbox COMPUESTO (Productos
/// = GestionarProductos + RecalcularStock; Gastos y facturas = RegistrarGastos +
/// RegistrarPagos) ahora tienen entrada propia porque el refactor los vuelve independientes
/// (Task 4/6) -- para esos cuatro, la etiqueta se tomó de otra pantalla existente que ya usa
/// exactamente ese permiso (ver comentario en cada entrada), nunca inventada:
///   - GestionarProductos -> "Productos", título de ProductoListView.axaml.
///   - RecalcularStock -> "Recalcular stock", texto del botón en MovimientoHistorialView.axaml.
///   - RegistrarGastos -> "Gastos y facturas", título de GastosView.axaml (la pantalla que
///     GastoFormViewModel.PuedeGuardar gatea con este mismo permiso).
///   - RegistrarPagos -> "Pagos de la factura", título de PagosGastoView.axaml (la pantalla
///     que PagosGastoViewModel.PuedeRegistrarPago gatea con este mismo permiso).
/// </summary>
public static class CatalogoPermisosPanel
{
    public record Entrada(string Permiso, string Etiqueta, string Grupo);

    public static readonly IReadOnlyList<Entrada> Entradas =
    [
        // ── Catálogo ─────────────────────────────────────────────────────────
        new(Permisos.GestionarProductos, "Productos", "Catálogo"),
        new(Permisos.RecalcularStock, "Recalcular stock", "Catálogo"),
        new(Permisos.GestionarTablasMaestras, "Tablas maestras (categorías, proveedores, unidades)", "Catálogo"),
        new(Permisos.RegistrarMovimientos, "Registrar movimientos de stock", "Catálogo"),

        // ── Finanzas ─────────────────────────────────────────────────────────
        new(Permisos.RegistrarGastos, "Gastos y facturas", "Finanzas"),
        new(Permisos.RegistrarPagos, "Pagos de la factura", "Finanzas"),
        new(Permisos.RegistrarIngresos, "Ingresos de caja", "Finanzas"),
        new(Permisos.VerFinanzas, "Ver Finanzas (libro caja, control POA, calendario)", "Finanzas"),
        new(Permisos.GestionarMaestrosFinanzas, "Maestros de finanzas (fuentes, rubros, líneas POA)", "Finanzas"),

        // ── Tareas y reportes ────────────────────────────────────────────────
        new(Permisos.GestionarTareas, "Tareas", "Tareas y reportes"),
        new(Permisos.VerReportes, "Reportes", "Tareas y reportes"),

        // ── Documentos ───────────────────────────────────────────────────────
        new(Permisos.GestionarDocumentos, "Documentos administrativos", "Documentos"),
    ];
}
