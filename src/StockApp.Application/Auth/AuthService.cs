using StockApp.Application.Interfaces;

namespace StockApp.Application.Auth;

public enum LoginError { UsuarioNoEncontrado, ContrasenaInvalida, UsuarioInactivo }

public record LoginResult(bool Exitoso, LoginError? Error = null)
{
    public static LoginResult Ok()                  => new(true);
    public static LoginResult Fallo(LoginError e)   => new(false, e);
}

public class AuthService : IAuthService
{
    private readonly IUsuarioRepository _repo;
    private readonly IPasswordHasher    _hasher;
    private readonly ICurrentSession    _session;
    private readonly IAuditLogger       _audit;

    public AuthService(
        IUsuarioRepository repo,
        IPasswordHasher    hasher,
        ICurrentSession    session,
        IAuditLogger       audit)
    {
        _repo    = repo;
        _hasher  = hasher;
        _session = session;
        _audit   = audit;
    }

    public async Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
    {
        var usuario = await _repo.BuscarPorNombreAsync(nombreUsuario);

        if (usuario is null)
            return LoginResult.Fallo(LoginError.UsuarioNoEncontrado);

        if (!usuario.Activo)
            return LoginResult.Fallo(LoginError.UsuarioInactivo);

        if (!_hasher.Verify(contrasena, usuario.HashContrasena))
            return LoginResult.Fallo(LoginError.ContrasenaInvalida);

        _session.IniciarSesion(usuario);
        await _repo.ActualizarUltimoAccesoAsync(usuario.Id, DateTime.UtcNow);

        return LoginResult.Ok();
    }

    public Task LogoutAsync()
    {
        _session.CerrarSesion();
        return Task.CompletedTask;
    }

    /// <summary>
    /// AuthService es el IAuthService server-side legacy (pre-migración a API, sin call sites
    /// activos en DI hoy — ver AuthEndpoints.MapPost("/login"), que implementa el login inline
    /// sin pasar por esta clase). No hace ningún request: PermisosActuales ya está poblado en
    /// ICurrentSession por quien corresponda en este modo (mismo criterio que IniciarSesion/
    /// CerrarSesion arriba, que tampoco hablan con la red).
    /// </summary>
    public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync()
        => Task.FromResult(_session.PermisosActuales);
}
