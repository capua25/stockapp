using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Auth;

/// <summary>
/// ABM de usuarios. Solo para Admin: todas las operaciones verifican autorización
/// antes de ejecutar. Nunca borra físicamente; usa baja lógica.
/// </summary>
public class UsuarioService : IUsuarioService
{
    private readonly IUsuarioRepository    _repo;
    private readonly IPasswordHasher       _hasher;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;
    private readonly IRevocadorTokens      _revocador;

    public UsuarioService(
        IUsuarioRepository    repo,
        IPasswordHasher       hasher,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit,
        IRevocadorTokens      revocador)
    {
        _repo      = repo;
        _hasher    = hasher;
        _session   = session;
        _auth      = auth;
        _audit     = audit;
        _revocador = revocador;
    }

    public async Task<int> AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto,
        string contrasenaPlan, RolUsuario rol)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        // Fix 6: validación mínima de contraseña
        ContrasenaValidator.Validar(contrasenaPlan);

        var nuevo = new Usuario
        {
            NombreUsuario  = nombreUsuario,
            NombreCompleto = nombreCompleto,
            HashContrasena = _hasher.Hash(contrasenaPlan),
            Rol            = rol,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        var id = await _repo.AgregarAsync(nuevo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreUsuario}' con rol {rol}");

        return id;
    }

    public async Task BajaLogicaAsync(int usuarioId)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        // Fix 2: no auto-baja
        if (usuarioId == _session.UsuarioActual!.Id)
            throw new ReglaDeNegocioException("Un usuario no puede darse de baja a sí mismo.");

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Fix 2: proteger último Admin activo
        if (usuario.Rol == RolUsuario.Admin && usuario.Activo)
        {
            var adminsActivos = await _repo.ContarAdminsActivosAsync();
            if (adminsActivos <= 1)
                throw new ReglaDeNegocioException(
                    "No se puede deshabilitar al último Admin activo del sistema.");
        }

        usuario.Activo = false;
        await _repo.ActualizarAsync(usuario);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaUsuario,
            "Usuario", usuarioId,
            $"Baja lógica de '{usuario.NombreUsuario}'");

        // Fase B hardening (deuda M3): un usuario deshabilitado no debe poder seguir
        // usando su JWT viejo hasta que expire naturalmente.
        _revocador.Revocar(usuarioId, DateTime.UtcNow);
    }

    public async Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        var rolAnterior = usuario.Rol;
        usuario.Rol = nuevoRol;
        await _repo.ActualizarAsync(usuario);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.CambioRol,
            "Usuario", usuarioId,
            $"Rol: {rolAnterior} → {nuevoRol}");

        // Fase B hardening (deuda M3): un JWT viejo con el rol anterior no debe seguir
        // siendo válido tras el cambio.
        _revocador.Revocar(usuarioId, DateTime.UtcNow);
    }

    /// <summary>
    /// Cambia la contraseña de un usuario.
    /// - Auto-cambio (usuarioId == sesión actual): REQUIERE <paramref name="contrasenaActualPlan"/> para verificar identidad.
    /// - Reset administrativo (Admin cambia la de otro): no requiere la contraseña actual del otro (reset mutuo, §5.1).
    /// </summary>
    public async Task CambiarContrasenaAsync(
        int usuarioId,
        string nuevaContrasenaPlan,
        string? contrasenaActualPlan = null)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        // Fix 6: validación mínima de contraseña
        ContrasenaValidator.Validar(nuevaContrasenaPlan);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Fix 7: auto-cambio requiere contraseña actual
        if (usuarioId == _session.UsuarioActual!.Id)
        {
            if (string.IsNullOrWhiteSpace(contrasenaActualPlan))
                throw new UnauthorizedAccessException(
                    "Para cambiar tu propia contraseña debés confirmar la contraseña actual.");

            if (!_hasher.Verify(contrasenaActualPlan, usuario.HashContrasena))
                throw new UnauthorizedAccessException(
                    "La contraseña actual no es correcta.");
        }

        usuario.HashContrasena = _hasher.Hash(nuevaContrasenaPlan);
        await _repo.ActualizarAsync(usuario);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.CambioContrasena,
            "Usuario", usuarioId,
            "Cambio de contraseña");

        // Fase B hardening: cualquier JWT de este usuario emitido antes de ahora queda
        // inválido de inmediato, sin esperar a su expiración natural. Aplica a ambos
        // caminos (auto-cambio y reset administrativo de otro usuario).
        _revocador.Revocar(usuarioId, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var usuarios = await _repo.ListarTodosAsync();
        return usuarios.Select(AUsuarioDto).ToList();
    }

    private static UsuarioDto AUsuarioDto(Usuario u) => new UsuarioDto(
        Id:             u.Id,
        NombreUsuario:  u.NombreUsuario,
        NombreCompleto: u.NombreCompleto,
        Rol:            u.Rol,
        Activo:         u.Activo,
        FechaAlta:      u.FechaAlta);
}
