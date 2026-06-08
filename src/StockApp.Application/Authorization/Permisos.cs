namespace StockApp.Application.Authorization;

/// <summary>
/// Nombres canónicos de las acciones protegidas del sistema.
/// Todos los servicios de Application usan estas constantes al llamar a IAuthorizationService.
/// </summary>
public static class Permisos
{
    public const string GestionarUsuarios    = "usuarios.gestionar";
    public const string VerReportes          = "reportes.ver";
    public const string GestionarCatalogo    = "catalogo.gestionar";
    public const string RegistrarMovimientos = "movimientos.registrar";
    public const string RecalcularStock      = "stock.recalcular";
}
