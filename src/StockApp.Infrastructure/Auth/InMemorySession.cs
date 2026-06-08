using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Infrastructure.Auth;

/// <summary>
/// Singleton en memoria. Hilo-seguro con lock simple (single-PC, sin concurrencia real,
/// pero la UI de Avalonia puede acceder desde hilos distintos).
/// Almacena un snapshot <see cref="UsuarioSesion"/> — nunca la entidad EF con el hash.
/// </summary>
public class InMemorySession : ICurrentSession
{
    private readonly object _lock = new();
    private UsuarioSesion? _sesionActual;

    public bool EstaAutenticado { get { lock (_lock) return _sesionActual != null; } }

    public UsuarioSesion? UsuarioActual { get { lock (_lock) return _sesionActual; } }

    public RolUsuario? RolActual { get { lock (_lock) return _sesionActual?.Rol; } }

    public void IniciarSesion(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var snapshot = new UsuarioSesion(
            usuario.Id,
            usuario.NombreUsuario,
            usuario.Rol,
            usuario.NombreCompleto);
        lock (_lock) _sesionActual = snapshot;
    }

    public void CerrarSesion()
    {
        lock (_lock) _sesionActual = null;
    }
}
