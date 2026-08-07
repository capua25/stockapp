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
        // Hallazgo 2: usa el mismo validador que AltaUsuarioAsync — antes esto solo
        // chequeaba IsNullOrWhiteSpace a mano y asignaba el nombre crudo, sin Trim ni cap
        // de 100. Vía BootstrapAdminSeeder, un "Bootstrap:AdminUser=\" admin \"" se
        // persistía con espacios: el login compara con == exacto y sin trim, así que
        // "admin" nunca entra y RequiereCrearAdminAsync ya da false para siempre —
        // sistema irrecuperable sin tocar la BD a mano. Un nombre >100 chars también
        // chocaba crudo contra el HasMaxLength(100) de EF con una DbUpdateException que
        // el catch de BootstrapAdminSeeder no matchea.
        var nombreNormalizado = NombreUsuarioValidator.ValidarYNormalizar(nombreUsuario);

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
                NombreUsuario  = nombreNormalizado,
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
