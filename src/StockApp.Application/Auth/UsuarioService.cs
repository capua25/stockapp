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
    private readonly IProveedorPermisos    _permisos;

    public UsuarioService(
        IUsuarioRepository    repo,
        IPasswordHasher       hasher,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit,
        IRevocadorTokens      revocador,
        IProveedorPermisos    permisos)
    {
        _repo      = repo;
        _hasher    = hasher;
        _session   = session;
        _auth      = auth;
        _audit     = audit;
        _revocador = revocador;
        _permisos  = permisos;
    }

    public async Task<int> AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto,
        string contrasenaPlan, RolUsuario rol)
    {
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        // Hallazgo 6: el nombre se valida primero — es el campo más primario del
        // formulario; con nombre vacío y contraseña corta a la vez, el error reportado
        // debe ser el del nombre.
        // Fix 3: valida y trimea NombreUsuario (vacío/whitespace o > 100 chars → 400,
        // no un 500 al chocar con el HasMaxLength(100) de EF).
        var nombreNormalizado = NombreUsuarioValidator.ValidarYNormalizar(nombreUsuario);

        // Fix 6: validación mínima de contraseña
        ContrasenaValidator.Validar(contrasenaPlan);

        // Hallazgo 1: sin esto, un rol fuera del enum (ej. {"rol":99} sin
        // JsonStringEnumConverter) pasaba el chequeo de solo-null del endpoint y quedaba
        // persistido con 201 Created — una fila inutilizable que nadie sabe por qué no anda.
        RolUsuarioValidator.ValidarDefinido(rol);

        // Fix 4a: chequeo previo del duplicado — camino normal, da un 409 con mensaje
        // claro. El índice único en BD + el catch en UsuarioRepository.AgregarAsync
        // (Fix 4b) cubren la carrera entre este chequeo y el insert.
        if (await _repo.BuscarPorNombreAsync(nombreNormalizado) is not null)
            throw new ReglaDeNegocioException(
                $"Ya existe un usuario con el nombre '{nombreNormalizado}'.");

        var nuevo = new Usuario
        {
            NombreUsuario  = nombreNormalizado,
            NombreCompleto = nombreCompleto,
            HashContrasena = _hasher.Hash(contrasenaPlan),
            Rol            = rol,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        var id = await _repo.AgregarAsync(nuevo);

        // Plantilla de arranque (spec decisión 3): sin este paso, todo Operador nuevo
        // arrancaría con cero permisos configurables — fail-closed correcto pero inútil en la
        // práctica. Admin nunca siembra nada acá: sus permisos son siempre todos, sin filas.
        if (rol == RolUsuario.Operador)
            await _permisos.GuardarAsync(id, AuthorizationService.PermisosInicialesOperador);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreNormalizado}' con rol {rol}");

        return id;
    }

    public async Task BajaLogicaAsync(int usuarioId)
    {
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

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
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        // Fix 1 / Hallazgo 1: rechazar valores fuera del enum (ej. casteos crudos desde el
        // endpoint). Regla centralizada en RolUsuarioValidator — AltaUsuarioAsync la usa
        // también, para no duplicar la definición.
        RolUsuarioValidator.ValidarDefinido(nuevoRol);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Fix 2: proteger último Admin activo (mismo criterio que BajaLogicaAsync). No
        // bloqueamos la auto-degradación en sí: a diferencia de la auto-baja (que te deja
        // sin cuenta usable), degradarte a vos mismo con otro Admin activo en el sistema
        // es una acción coherente (cesión de rol) — el chequeo de abajo ya cubre el único
        // caso realmente peligroso (quedar sin ningún Admin activo).
        //
        // Hallazgo 3 (conocido, NO arreglado): esto es check-then-act sin lock a nivel
        // fila ni RowVersion en Usuario. Con 2 Admins activos, dos PUT /rol concurrentes
        // (o un PUT /rol cruzado con un DELETE /usuarios/{id}) pueden leer ambos
        // ContarAdminsActivosAsync() == 2, pasar los dos, y dejar el sistema con cero
        // Admins activos. Mismo patrón preexistente que en BajaLogicaAsync — esta rama
        // duplica la superficie, no la introduce. Arreglarlo requiere un UPDATE
        // condicional o un lock a nivel fila (otro trabajo). Si el sistema queda sin
        // Admin activo, la recuperación es vía ServicioResetAdmin, no reintentando esto.
        if (usuario.Rol == RolUsuario.Admin && usuario.Activo && nuevoRol != RolUsuario.Admin)
        {
            var adminsActivos = await _repo.ContarAdminsActivosAsync();
            if (adminsActivos <= 1)
                throw new ReglaDeNegocioException(
                    "No se puede quitarle el rol de Admin al último Admin activo del sistema.");
        }

        var rolAnterior = usuario.Rol;
        usuario.Rol = nuevoRol;
        await _repo.ActualizarAsync(usuario);

        // Task 12 (permisos por operador — cierre del gap de cambio de rol): AltaUsuarioAsync
        // (Task 11) ya siembra PermisosInicialesOperador para el alta directa, pero degradar
        // un Admin a Operador es una tercera vía de creación de Operadores que hasta acá nacía
        // con cero permisos — mismo agujero, reabierto por esta puerta. Solo se siembra si el
        // usuario NO tiene filas: así no se pisa una configuración que un Admin armó a mano
        // antes de una promoción previa (nunca destruimos configuración, nunca dejamos a un
        // Operador sin nada). Al revés (Operador → Admin) no se toca nada a propósito: las
        // filas quedan huérfanas pero inertes (Admin nunca consulta la tabla, bypass total), y
        // así sobreviven intactas si más adelante lo vuelven a degradar.
        if (rolAnterior == RolUsuario.Admin && nuevoRol == RolUsuario.Operador)
        {
            var permisosActuales = await _permisos.ObtenerAsync(usuarioId);
            if (permisosActuales.Count == 0)
                await _permisos.GuardarAsync(usuarioId, AuthorizationService.PermisosInicialesOperador);
        }

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
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

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
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        var usuarios = await _repo.ListarTodosAsync();
        return usuarios.Select(AUsuarioDto).ToList();
    }

    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId)
    {
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Admin siempre tiene los 11 configurables — no hace falta consultar el proveedor
        // (y de hecho no debería haber filas: nunca se le escriben, spec decisión 3).
        if (usuario.Rol == RolUsuario.Admin)
            return AuthorizationService.PermisosConfigurables;

        var permisos = await _permisos.ObtenerAsync(usuarioId);
        return permisos.ToList();
    }

    public async Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos)
    {
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // El servidor no confía en que el cliente deshabilite el panel de permisos para Admin
        // (spec, endpoint de administración): lo valida también del lado seguro.
        if (usuario.Rol == RolUsuario.Admin)
            throw new ArgumentException(
                "No se pueden configurar permisos para un usuario Admin: tiene acceso total.");

        // Defensa contra un cliente viejo o manipulado intentando colar un permiso estructural
        // (ej. GestionarUsuarios) — nunca deberían estar en la whitelist de configurables.
        var fueraDeWhitelist = permisos.Where(p => !AuthorizationService.PermisosConfigurables.Contains(p)).ToList();
        if (fueraDeWhitelist.Count > 0)
            throw new ArgumentException(
                $"Los siguientes permisos no son configurables: {string.Join(", ", fueraDeWhitelist)}.");

        // Fix (revisión adversarial post-Task 10): un permiso repetido en la entrada (doble
        // click, cliente que reintenta) llegaba tal cual hasta
        // PermisoUsuarioRepository.ReemplazarPermisosAsync, que inserta una fila por elemento
        // sin deduplicar — dos INSERT idénticos violan el índice único (UsuarioId, Permiso) y
        // el DbUpdateException sin catch caía al 500 genérico. Se deduplica en SILENCIO acá,
        // el punto de entrada del input no confiable: enviar el mismo permiso dos veces
        // expresa la misma intención que enviarlo una vez y el estado final es idéntico —
        // rechazarlo con 400 sería pedante y la operación tiene que ser idempotente.
        var permisosUnicos = permisos.Distinct().ToList();

        await _permisos.GuardarAsync(usuarioId, permisosUnicos);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionPermisosUsuario,
            "Usuario", usuarioId,
            $"Permisos actualizados: {string.Join(", ", permisosUnicos)}");
    }

    private static UsuarioDto AUsuarioDto(Usuario u) => new UsuarioDto(
        Id:             u.Id,
        NombreUsuario:  u.NombreUsuario,
        NombreCompleto: u.NombreCompleto,
        Rol:            u.Rol,
        Activo:         u.Activo,
        FechaAlta:      u.FechaAlta);
}
