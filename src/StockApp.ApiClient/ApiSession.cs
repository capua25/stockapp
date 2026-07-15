using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

/// <summary>
/// Sesión del cliente API (spec 3b): reemplaza a InMemorySession en el desktop. Singleton.
/// Se puebla desde el LoginResponse enriquecido (3a, D8) vía EstablecerSesion; además del
/// snapshot de identidad guarda el token JWT que AuthTokenHandler adjunta a cada request.
/// Hilo-segura con lock simple, igual que InMemorySession (la UI de Avalonia puede acceder
/// desde hilos distintos).
/// </summary>
public class ApiSession : ICurrentSession
{
    private readonly object _lock = new();
    private UsuarioSesion? _sesionActual;
    private string? _token;

    /// <summary>
    /// El servidor respondió 401 a un request que llevaba token: la sesión venció.
    /// La composition root lo cablea a la navegación al login con aviso (App.axaml.cs).
    /// </summary>
    public event Action? SesionVencida;

    /// <summary>
    /// El servidor respondió 423 Locked: la licencia se desactivó (ej. borraron licencia.lic
    /// con la app abierta). La composition root lo cablea a la pantalla de bloqueo.
    /// </summary>
    public event Action? LicenciaDesactivada;

    public bool EstaAutenticado { get { lock (_lock) return _sesionActual != null; } }

    public UsuarioSesion? UsuarioActual { get { lock (_lock) return _sesionActual; } }

    public RolUsuario? RolActual { get { lock (_lock) return _sesionActual?.Rol; } }

    /// <summary>Token JWT vigente, o null si no hay sesión.</summary>
    public string? Token { get { lock (_lock) return _token; } }

    /// <summary>
    /// Miembro de ICurrentSession (proyección entidad → snapshot, igual que InMemorySession).
    /// En modo API nadie lo llama — el login usa <see cref="EstablecerSesion"/> — pero se
    /// implementa funcional para honrar el contrato. No establece token.
    /// </summary>
    public void IniciarSesion(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var snapshot = new UsuarioSesion(
            usuario.Id,
            usuario.NombreUsuario,
            usuario.Rol,
            usuario.NombreCompleto);
        lock (_lock)
        {
            _sesionActual = snapshot;
            _token        = null;
        }
    }

    /// <summary>Establece la sesión desde el LoginResponse de la API (snapshot + token JWT).</summary>
    public void EstablecerSesion(UsuarioSesion usuario, string token)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        lock (_lock)
        {
            _sesionActual = usuario;
            _token        = token;
        }
    }

    public void CerrarSesion()
    {
        lock (_lock)
        {
            _sesionActual = null;
            _token        = null;
        }
    }

    /// <summary>Lo invoca AuthTokenHandler ante un 401 con token (internal + InternalsVisibleTo).</summary>
    internal void DispararSesionVencida() => SesionVencida?.Invoke();

    /// <summary>Lo invoca AuthTokenHandler ante un 423 (internal + InternalsVisibleTo).</summary>
    internal void DispararLicenciaDesactivada() => LicenciaDesactivada?.Invoke();
}
