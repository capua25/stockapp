using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// Detecta si la BD no tiene ningún usuario y, en ese caso, orquesta la creación
/// del primer Admin. No define una contraseña por defecto: la elige el usuario en ese momento.
/// </summary>
public class PrimerArranqueService
{
    private readonly IUsuarioRepository _repo;
    private readonly IPasswordHasher    _hasher;

    public PrimerArranqueService(IUsuarioRepository repo, IPasswordHasher hasher)
    {
        _repo   = repo;
        _hasher = hasher;
    }

    /// <summary>true si no hay ningún usuario en la BD.</summary>
    public async Task<bool> RequiereCrearAdminAsync()
        => !await _repo.ExisteAlgunUsuarioAsync();

    /// <summary>
    /// Crea el primer usuario Admin con la contraseña provista (hasheada).
    /// Lanza <see cref="InvalidOperationException"/> si ya existe al menos un usuario.
    /// </summary>
    public async Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
    {
        if (!await RequiereCrearAdminAsync())
            throw new InvalidOperationException(
                "No se puede crear el Admin inicial: ya existen usuarios en la base de datos.");

        var admin = new Usuario
        {
            NombreUsuario  = nombreUsuario,
            HashContrasena = _hasher.Hash(contrasenaPlana),
            Rol            = RolUsuario.Admin,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        await _repo.AgregarAsync(admin);
    }
}
