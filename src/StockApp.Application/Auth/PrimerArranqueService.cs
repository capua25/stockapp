using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Auth;

/// <summary>
/// Detecta si la BD no tiene ningún usuario y, en ese caso, orquesta la creación
/// del primer Admin. No define una contraseña por defecto: la elige el usuario en ese momento.
/// </summary>
public class PrimerArranqueService : IPrimerArranqueService
{
    private readonly IUsuarioRepository _repo;
    private readonly IPasswordHasher    _hasher;

    // Fix 5: protege el TOCTOU de check-then-act en CrearAdminInicialAsync
    private static readonly SemaphoreSlim _semaforo = new(1, 1);

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
    /// Lanza <see cref="ReglaDeNegocioException"/> si ya existe al menos un usuario.
    /// El semáforo garantiza que dos llamadas concurrentes no creen dos Admins.
    /// </summary>
    public async Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
    {
        // Fix review Fase 3a: sin esto, un nombreUsuario en blanco vía POST /auth/primer-admin
        // crea el Admin génesis con username whitespace; el login lo rechaza con 400 y
        // requiereCrearAdmin queda false para siempre (sistema irrecuperable sin tocar la BD).
        if (string.IsNullOrWhiteSpace(nombreUsuario))
            throw new ArgumentException("El nombre de usuario es obligatorio.");

        // Fix 6: validación mínima de contraseña
        ContrasenaValidator.Validar(contrasenaPlana);

        await _semaforo.WaitAsync();
        try
        {
            if (!await RequiereCrearAdminAsync())
                throw new ReglaDeNegocioException(
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
        finally
        {
            _semaforo.Release();
        }
    }
}
