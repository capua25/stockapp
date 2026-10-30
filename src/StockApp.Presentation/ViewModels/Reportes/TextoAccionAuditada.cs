using System.Collections.Generic;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>
/// Texto legible de una acción del log de auditoría ("Registro de movimiento", no "RegistroMovimiento"). Único lugar:
/// lo usan la grilla del Log de auditoría (<c>AccionAuditadaConverter</c>) y el PDF del mismo reporte.
/// </summary>
public static class TextoAccionAuditada
{
    private static readonly IReadOnlyDictionary<AccionAuditada, string> Textos = new Dictionary<AccionAuditada, string>
    {
        [AccionAuditada.CambioPrecio] = "Cambio de precio",
        [AccionAuditada.AltaProducto] = "Alta de producto",
        [AccionAuditada.BajaProducto] = "Baja de producto",
        [AccionAuditada.AltaUsuario] = "Alta de usuario",
        [AccionAuditada.BajaUsuario] = "Baja de usuario",
        [AccionAuditada.CambioRol] = "Cambio de rol",
        [AccionAuditada.CambioContrasena] = "Cambio de contraseña",
        [AccionAuditada.AltaCategoria] = "Alta de categoría",
        [AccionAuditada.BajaCategoria] = "Baja de categoría",
        [AccionAuditada.ModificacionCategoria] = "Modificación de categoría",
        [AccionAuditada.AltaProveedor] = "Alta de proveedor",
        [AccionAuditada.BajaProveedor] = "Baja de proveedor",
        [AccionAuditada.ModificacionProveedor] = "Modificación de proveedor",
        [AccionAuditada.AltaUnidadMedida] = "Alta de unidad de medida",
        [AccionAuditada.BajaUnidadMedida] = "Baja de unidad de medida",
        [AccionAuditada.ModificacionUnidadMedida] = "Modificación de unidad de medida",
        [AccionAuditada.ModificacionProducto] = "Modificación de producto",
        [AccionAuditada.RegistroMovimiento] = "Registro de movimiento",
        [AccionAuditada.RecalculoStock] = "Recálculo de stock",
        [AccionAuditada.ActivacionLicencia] = "Activación de licencia",
        [AccionAuditada.IntentoActivacionLicenciaFallido] = "Intento de activación de licencia fallido",
        [AccionAuditada.ResetAdminFirmado] = "Reinicio de administrador firmado",
        [AccionAuditada.AltaFuenteFinanciamiento] = "Alta de fuente de financiamiento",
        [AccionAuditada.ModificacionFuenteFinanciamiento] = "Modificación de fuente de financiamiento",
        [AccionAuditada.BajaFuenteFinanciamiento] = "Baja de fuente de financiamiento",
        [AccionAuditada.AltaRubroGasto] = "Alta de rubro de gasto",
        [AccionAuditada.ModificacionRubroGasto] = "Modificación de rubro de gasto",
        [AccionAuditada.BajaRubroGasto] = "Baja de rubro de gasto",
        [AccionAuditada.AltaLineaPoa] = "Alta de línea POA",
        [AccionAuditada.ModificacionLineaPoa] = "Modificación de línea POA",
        [AccionAuditada.BajaLineaPoa] = "Baja de línea POA",
        [AccionAuditada.AltaGasto] = "Alta de gasto",
        [AccionAuditada.ModificacionGasto] = "Modificación de gasto",
        [AccionAuditada.AnulacionGasto] = "Anulación de gasto",
        [AccionAuditada.AltaPagoGasto] = "Alta de pago de gasto",
        [AccionAuditada.AnulacionPagoGasto] = "Anulación de pago de gasto",
        [AccionAuditada.AltaIngresoCaja] = "Alta de ingreso de caja",
        [AccionAuditada.ModificacionIngresoCaja] = "Modificación de ingreso de caja",
        [AccionAuditada.BajaIngresoCaja] = "Baja de ingreso de caja",
        [AccionAuditada.AsociacionMovimientosAGasto] = "Asociación de movimientos a gasto",
        [AccionAuditada.AltaAdjunto] = "Alta de adjunto",
        [AccionAuditada.BajaAdjunto] = "Baja de adjunto",
        [AccionAuditada.ImportacionPlanillas] = "Importación de planillas",
        [AccionAuditada.ReversionImportacion] = "Reversión de importación",
        [AccionAuditada.IngresoPorFactura] = "Ingreso por factura",
        [AccionAuditada.AnulacionIngresoPorFactura] = "Anulación de ingreso por factura",
        [AccionAuditada.AltaTarea] = "Alta de tarea",
        [AccionAuditada.CambioEstadoTarea] = "Cambio de estado de tarea",
        [AccionAuditada.CambioPrioridadTarea] = "Cambio de prioridad de tarea",
        [AccionAuditada.CancelacionTarea] = "Cancelación de tarea",
        [AccionAuditada.AltaNotaTarea] = "Alta de nota de tarea",
        [AccionAuditada.ModificacionPermisosUsuario] = "Modificación de permisos de usuario",
        [AccionAuditada.AltaDocumentoAdministrativo] = "Alta de documento administrativo",
        [AccionAuditada.CambioEstadoDocumento] = "Cambio de estado de documento",
        [AccionAuditada.ReaperturaDocumento] = "Reapertura de documento",
        [AccionAuditada.AnulacionDocumento] = "Anulación de documento",
        [AccionAuditada.AltaNotaDocumento] = "Alta de nota de documento",
        [AccionAuditada.AltaAdjuntoDocumento] = "Alta de adjunto de documento",
        [AccionAuditada.BajaAdjuntoDocumento] = "Baja de adjunto de documento",
        [AccionAuditada.EdicionDocumento] = "Edición de documento",
        [AccionAuditada.AltaZona] = "Alta de zona",
        [AccionAuditada.BajaZona] = "Baja de zona",
        [AccionAuditada.ModificacionZona] = "Modificación de zona",
        [AccionAuditada.AltaDimensionTematica] = "Alta de dimensión temática",
        [AccionAuditada.BajaDimensionTematica] = "Baja de dimensión temática",
        [AccionAuditada.ModificacionDimensionTematica] = "Modificación de dimensión temática",
        [AccionAuditada.AltaOrganismoResponsable] = "Alta de organismo responsable",
        [AccionAuditada.BajaOrganismoResponsable] = "Baja de organismo responsable",
        [AccionAuditada.ModificacionOrganismoResponsable] = "Modificación de organismo responsable",
        [AccionAuditada.AltaOrigenFinanciamiento] = "Alta de origen de financiamiento",
        [AccionAuditada.BajaOrigenFinanciamiento] = "Baja de origen de financiamiento",
        [AccionAuditada.ModificacionOrigenFinanciamiento] = "Modificación de origen de financiamiento",
        [AccionAuditada.ReclasificacionTarea] = "Reclasificación de tarea",
        [AccionAuditada.AltaProyecto] = "Alta de proyecto",
        [AccionAuditada.ModificacionProyecto] = "Modificación de proyecto",
        [AccionAuditada.CambioEstadoProyecto] = "Cambio de estado de proyecto",
        [AccionAuditada.AltaNotaProyecto] = "Nota en proyecto",
    };

    /// <summary>El texto de <paramref name="accion"/>; un valor que esta versión no conoce (la base puede traer acciones
    /// de una versión más nueva) se muestra con su nombre, sin romper la grilla.</summary>
    public static string De(AccionAuditada accion) => Textos.TryGetValue(accion, out var texto) ? texto : accion.ToString();
}
