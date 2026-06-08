using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Infrastructure.Auth;

/// <summary>
/// Singleton en memoria. Hilo-seguro con lock simple (single-PC, sin concurrencia real,
/// pero la UI de Avalonia puede acceder desde hilos distintos).
/// </summary>
public class InMemorySession : ICurrentSession
{
    private readonly object _lock = new();
    private Usuario? _usuarioActual;

    public bool EstaAutenticado { get { lock (_lock) return _usuarioActual != null; } }

    public Usuario? UsuarioActual { get { lock (_lock) return _usuarioActual; } }

    public RolUsuario? RolActual { get { lock (_lock) return _usuarioActual?.Rol; } }

    public void IniciarSesion(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        lock (_lock) _usuarioActual = usuario;
    }

    public void CerrarSesion()
    {
        lock (_lock) _usuarioActual = null;
    }
}
