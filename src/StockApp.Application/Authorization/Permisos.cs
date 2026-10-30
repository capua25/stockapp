namespace StockApp.Application.Authorization;

/// <summary>
/// Nombres canónicos de las acciones protegidas del sistema.
/// Todos los servicios de Application usan estas constantes al llamar a IAuthorizationService.
/// </summary>
public static class Permisos
{
    public const string GestionarUsuarios       = "usuarios.gestionar";
    public const string VerReportes             = "reportes.ver";
    public const string GestionarProductos      = "catalogo.productos";
    public const string GestionarTablasMaestras = "catalogo.maestras";
    public const string RegistrarMovimientos    = "movimientos.registrar";
    public const string RecalcularStock         = "stock.recalcular";

    // Finanzas — Fases 1 y 2: por ahora Admin Y Operador tienen todos (spec Finanzas §9);
    // el futuro sistema de permisos por usuario solo cambia el mapeo rol→permiso.
    public const string VerFinanzas              = "finanzas.ver";
    public const string GestionarMaestrosFinanzas = "finanzas.maestros";
    public const string RegistrarGastos           = "finanzas.gastos";
    public const string RegistrarPagos            = "finanzas.pagos";
    public const string RegistrarIngresos         = "finanzas.ingresos";

    // Backups programados (Entrega 1) — Admin-only desde el vamos: superficie sensible
    // (backups y, en la Entrega 2, logs del servidor).
    public const string GestionarDiagnostico = "diagnostico.gestionar";

    // Tareas (spec 2026-08-01) — módulo independiente. GestionarTareas: crear, tomar,
    // soltar, terminar, comentar — Admin Y Operador. AdministrarTareas (Task 5): cancelar
    // y cambiar prioridad — solo Admin.
    public const string GestionarTareas = "tareas.gestionar";

    /// <summary>Cancelar y cambiar prioridad: decide sobre trabajo que otro cargó — solo Admin.</summary>
    public const string AdministrarTareas = "tareas.administrar";

    // Documentos administrativos (spec 2026-08-11) — módulo independiente. GestionarDocumentos:
    // registrar, editar, listar, transicionar (iniciar/volver a pendiente/finalizar), notas y
    // adjuntos (agregar/listar/descargar) — Admin Y Operador. AdministrarDocumentos: anular,
    // reabrir y quitar adjuntos — solo Admin, mismo criterio que AdministrarTareas.
    public const string GestionarDocumentos = "documentos.gestionar";

    /// <summary>Anular, reabrir y quitar adjuntos: decide sobre el cierre/apertura de un trámite y sobre evidencia documental ya cargada — solo Admin.</summary>
    public const string AdministrarDocumentos = "documentos.administrar";

    // Proyectos (spec 2026-10-30) — módulo independiente. Ambos configurables (no estructurales
    // Admin): VerProyectos habilita la pantalla; GestionarProyectos requiere VerProyectos.
    public const string VerProyectos = "proyectos.ver";
    public const string GestionarProyectos = "proyectos.gestionar";

    /// <summary>
    /// Lista explícita de todos los permisos del sistema (sin reflection). Consumida por
    /// StockApp.Api/Program.cs (Fase 2b, D1) para derivar las políticas de autorización
    /// HTTP a partir de AuthorizationService, en vez de declararlas a mano por recurso.
    /// </summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        GestionarUsuarios,
        VerReportes,
        GestionarProductos,
        GestionarTablasMaestras,
        RegistrarMovimientos,
        RecalcularStock,
        VerFinanzas,
        GestionarMaestrosFinanzas,
        RegistrarGastos,
        RegistrarPagos,
        RegistrarIngresos,
        GestionarDiagnostico,
        GestionarTareas,
        AdministrarTareas,
        GestionarDocumentos,
        AdministrarDocumentos,
        VerProyectos,
        GestionarProyectos,
    ];
}
