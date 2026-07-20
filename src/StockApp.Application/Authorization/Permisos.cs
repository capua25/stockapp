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

    // Finanzas — F5b: a diferencia de los permisos de arriba, este es Admin-only desde
    // el vamos (no espera el futuro sistema de permisos por usuario). Importar planillas
    // reemplaza datos históricos de todo el ejercicio; Operador queda afuera por diseño.
    public const string ImportarPlanillas         = "finanzas.importar";

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
        ImportarPlanillas,
    ];
}
