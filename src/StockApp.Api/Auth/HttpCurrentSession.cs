using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

/// <summary>
/// ICurrentSession construida por request a partir de los claims del JWT ya validado.
/// Reemplaza a InMemorySession SOLO en el grafo de DI de StockApp.Api; InMemorySession
/// sigue en uso, sin cambios, en la composición root de la app desktop (App.axaml.cs).
/// No admite mutación: el JWT de 2a solo lleva usuarioId y rol (spec §2), así que
/// UsuarioActual.NombreUsuario/NombreCompleto quedan vacíos — ningún endpoint del
/// slice de 2a los consume (solo RolActual, vía AuthorizationService.Verificar).
/// </summary>
public class HttpCurrentSession : ICurrentSession
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentSession(IHttpContextAccessor accessor) => _accessor = accessor;

    public bool EstaAutenticado => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public UsuarioSesion? UsuarioActual
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user is null || user.Identity?.IsAuthenticated != true)
                return null;

            var idClaim = user.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
            var rolClaim = user.FindFirst(StockAppClaimTypes.Rol)?.Value;

            if (idClaim is null || rolClaim is null)
                return null;

            return new UsuarioSesion(
                int.Parse(idClaim),
                NombreUsuario: string.Empty,
                Enum.Parse<RolUsuario>(rolClaim),
                NombreCompleto: null);
        }
    }

    public RolUsuario? RolActual
    {
        get
        {
            var rolClaim = _accessor.HttpContext?.User.FindFirst(StockAppClaimTypes.Rol)?.Value;
            return rolClaim is null ? null : Enum.Parse<RolUsuario>(rolClaim);
        }
    }

    public void IniciarSesion(Usuario usuario) =>
        throw new NotSupportedException(
            "HttpCurrentSession se arma desde los claims del JWT por request; no admite " +
            "IniciarSesion. El login emite un token nuevo en vez de mutar una sesión existente.");

    public void CerrarSesion() =>
        throw new NotSupportedException(
            "HttpCurrentSession se arma desde los claims del JWT por request; no admite " +
            "CerrarSesion. El cliente descarta el token para 'cerrar sesión'.");
}
