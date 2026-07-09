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
    ];
}
