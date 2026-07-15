using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Auth;

public class UsuarioServiceTests
{
    private static UsuarioSesion SesionAdmin(int id = 1) =>
        new(id, "admin", RolUsuario.Admin, null);

    private static (UsuarioService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock,
                    Mock<IRevocadorTokens> revocadorMock)
        Crear(RolUsuario rolSesion = RolUsuario.Admin, int idSesion = 1)
    {
        var repo       = new Mock<IUsuarioRepository>();
        var hasher     = new Mock<IPasswordHasher>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();
        var audit      = new Mock<IAuditLogger>();
        var revocador  = new Mock<IRevocadorTokens>();

        session.Setup(s => s.RolActual).Returns(rolSesion);
        session.Setup(s => s.UsuarioActual).Returns(SesionAdmin(idSesion));
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        // Admin: Verificar no lanza
        if (rolSesion == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarUsuarios))
                .Throws<UnauthorizedAccessException>();

        var svc = new UsuarioService(
            repo.Object, hasher.Object, session.Object, auth.Object, audit.Object, revocador.Object);
        return (svc, repo, hasher, session, auth, audit, revocador);
    }

    [Fact]
    public async Task AltaUsuario_Admin_CreaConHashYEventoAuditoria_DevuelveId()
    {
        var (svc, repo, hasher, session, _, audit, _) = Crear();
        repo.Setup(r => r.AgregarAsync(It.IsAny<Usuario>())).ReturnsAsync(42);

        var id = await svc.AltaUsuarioAsync("operador2", "Nombre Completo", "pwd123", RolUsuario.Operador);

        Assert.Equal(42, id);
        hasher.Verify(h => h.Hash("pwd123"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "operador2" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Operador &&
            u.Activo == true
        )), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaUsuario,
            "Usuario", 42, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogica_Admin_PoneActivoFalseYNoEliminaRegistro()
    {
        var usuario = new Usuario
        {
            Id = 5, NombreUsuario = "operador1", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, session, _, audit, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(usuario);
        repo.Setup(r => r.ContarAdminsActivosAsync()).ReturnsAsync(1);

        await svc.BajaLogicaAsync(5);

        // Se actualizó, NO se eliminó
        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaUsuario,
            "Usuario", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogica_NoSePuedeAutoEliminar()
    {
        var usuario = new Usuario
        {
            Id = 1, NombreUsuario = "admin", HashContrasena = "h",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, _, _, _, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(usuario);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(1));
    }

    [Fact]
    public async Task BajaLogica_NoSePuedeDeshabilitarUltimoAdmin()
    {
        var usuario = new Usuario
        {
            Id = 2, NombreUsuario = "admin2", HashContrasena = "h",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, _, _, _, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(usuario);
        repo.Setup(r => r.ContarAdminsActivosAsync()).ReturnsAsync(1); // único Admin activo

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task BajaLogica_Admin_ConOtroAdminActivo_Funciona()
    {
        var usuario = new Usuario
        {
            Id = 2, NombreUsuario = "admin2", HashContrasena = "h",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, _, _, audit, revocador) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(usuario);
        repo.Setup(r => r.ContarAdminsActivosAsync()).ReturnsAsync(2); // hay otro Admin

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Activo == false)), Times.Once);
    }

    [Fact]
    public async Task BajaLogica_Admin_RevocaLosTokensDelUsuarioDeshabilitado()
    {
        // Deuda M3 (hardening Fase B): un usuario deshabilitado no debe poder seguir
        // usando su JWT viejo hasta que expire naturalmente.
        var usuario = new Usuario
        {
            Id = 5, NombreUsuario = "operador1", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, _, _, _, revocador) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(usuario);
        repo.Setup(r => r.ContarAdminsActivosAsync()).ReturnsAsync(1);

        await svc.BajaLogicaAsync(5);

        revocador.Verify(r => r.Revocar(5, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task AltaUsuario_Operador_LanzaUnauthorized()
    {
        var (svc, _, _, _, _, _, _) = Crear(rolSesion: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaUsuarioAsync("x", null, "pwd123", RolUsuario.Operador));
    }

    [Fact]
    public async Task CambioRol_Admin_ActualizaYRegistraAuditoria()
    {
        var usuario = new Usuario
        {
            Id = 3, NombreUsuario = "alguien", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, session, _, audit, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(usuario);

        await svc.CambiarRolAsync(3, RolUsuario.Admin);

        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Rol == RolUsuario.Admin)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioRol, "Usuario", 3, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambioRol_Admin_RevocaLosTokensDelUsuarioAfectado()
    {
        // Deuda M3 (hardening Fase B): un cambio de rol (p.ej. degradar de Admin a
        // Operador) no debe convivir con un JWT viejo que todavía lleve el rol anterior.
        var usuario = new Usuario
        {
            Id = 3, NombreUsuario = "alguien", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, _, _, _, revocador) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(usuario);

        await svc.CambiarRolAsync(3, RolUsuario.Admin);

        revocador.Verify(r => r.Revocar(3, It.IsAny<DateTime>()), Times.Once);
    }


    // ── CambiarContrasenaAsync (Fix 7) ──────────────────────────────────────

    [Fact]
    public async Task CambioContrasena_AdminReseteandoOtroUsuario_NoRequiereContrasenaActual()
    {
        // Admin (id=1) resetea la contraseña de otro usuario (id=4) — no requiere contrasenaActual
        var usuario = new Usuario
        {
            Id = 4, NombreUsuario = "alguien", HashContrasena = "hash_viejo",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, hasher, session, _, audit, revocador) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(usuario);

        await svc.CambiarContrasenaAsync(4, "nuevaContrasena123");

        hasher.Verify(h => h.Hash("nuevaContrasena123"), Times.Once);
        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u =>
            u.HashContrasena == "$2a$12$hashed")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioContrasena, "Usuario", 4, It.IsAny<string>()), Times.Once);
        // Fase B hardening: revocar los JWTs viejos del usuario reseteado (id=4), no del admin.
        revocador.Verify(r => r.Revocar(4, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task CambioContrasena_AutoCambio_SinContrasenaActual_LanzaUnauthorized()
    {
        // El usuario (id=1) intenta cambiar su propia contraseña sin proveer la actual
        var (svc, repo, _, _, _, _, _) = Crear(idSesion: 1);
        var usuario = new Usuario
        {
            Id = 1, NombreUsuario = "admin", HashContrasena = "hash_actual",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(usuario);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CambiarContrasenaAsync(1, "nuevaContrasena123", contrasenaActualPlan: null));
    }

    [Fact]
    public async Task CambioContrasena_AutoCambio_ContrasenaActualIncorrecta_LanzaUnauthorized()
    {
        var usuario = new Usuario
        {
            Id = 1, NombreUsuario = "admin", HashContrasena = "hash_actual",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, hasher, _, _, _, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(usuario);
        hasher.Setup(h => h.Verify("contrasenaIncorrecta", "hash_actual")).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.CambiarContrasenaAsync(1, "nuevaContrasena123", "contrasenaIncorrecta"));
    }

    [Fact]
    public async Task CambioContrasena_AutoCambio_ContrasenaActualCorrecta_Funciona()
    {
        var usuario = new Usuario
        {
            Id = 1, NombreUsuario = "admin", HashContrasena = "hash_actual",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, hasher, _, _, audit, revocador) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(usuario);
        hasher.Setup(h => h.Verify("contrasenaCorrecta", "hash_actual")).Returns(true);

        await svc.CambiarContrasenaAsync(1, "nuevaContrasena123", "contrasenaCorrecta");

        hasher.Verify(h => h.Hash("nuevaContrasena123"), Times.Once);
        repo.Verify(r => r.ActualizarAsync(It.IsAny<Usuario>()), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioContrasena, "Usuario", 1, It.IsAny<string>()), Times.Once);
        revocador.Verify(r => r.Revocar(1, It.IsAny<DateTime>()), Times.Once);
    }

    // ── Fix 6: validación mínima de contraseña ────────────────────────────────

    [Fact]
    public async Task AltaUsuario_ContrasenaVacia_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaUsuarioAsync("nuevo", null, "", RolUsuario.Operador));
    }

    [Fact]
    public async Task AltaUsuario_ContrasenaConMenosDe6Chars_LanzaArgumentException()
    {
        var (svc, _, _, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaUsuarioAsync("nuevo", null, "12345", RolUsuario.Operador));
    }

    [Fact]
    public async Task AltaUsuario_ContrasenaConExactamente6Chars_Funciona()
    {
        var (svc, repo, _, _, _, _, _) = Crear();

        await svc.AltaUsuarioAsync("nuevo", null, "123456", RolUsuario.Operador);

        repo.Verify(r => r.AgregarAsync(It.IsAny<Usuario>()), Times.Once);
    }

    [Fact]
    public async Task CambioContrasena_NuevaContrasenaConMenosDe6Chars_LanzaArgumentException()
    {
        var usuario = new Usuario
        {
            Id = 4, NombreUsuario = "alguien", HashContrasena = "hash_viejo",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, _, _, _, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(usuario);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CambiarContrasenaAsync(4, "12345"));
    }

    // ── ListarAsync (Fase 2b, D6) ───────────────────────────────────────────

    [Fact]
    public async Task ListarAsync_Admin_DevuelveDtosSinHashContrasena()
    {
        var (svc, repo, _, _, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodosAsync()).ReturnsAsync(new List<Usuario>
        {
            new()
            {
                Id = 1, NombreUsuario = "admin", NombreCompleto = "Admin Uno",
                HashContrasena = "hash-secreto", Rol = RolUsuario.Admin,
                Activo = true, FechaAlta = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new()
            {
                Id = 2, NombreUsuario = "operador1", NombreCompleto = null,
                HashContrasena = "otro-hash", Rol = RolUsuario.Operador,
                Activo = false, FechaAlta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        });

        var resultado = await svc.ListarAsync();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, u => u.Id == 1 && u.NombreUsuario == "admin"
            && u.NombreCompleto == "Admin Uno" && u.Rol == RolUsuario.Admin && u.Activo);
        Assert.Contains(resultado, u => u.Id == 2 && u.NombreUsuario == "operador1"
            && u.NombreCompleto == null && u.Rol == RolUsuario.Operador && !u.Activo);
    }

    [Fact]
    public async Task ListarAsync_Operador_LanzaUnauthorized()
    {
        var (svc, _, _, _, _, _, _) = Crear(rolSesion: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarAsync());
    }

    // ─── EntidadNoEncontradaException (Fase 3a, D4) ─────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _, _) = Crear(idSesion: 1);
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    [Fact]
    public async Task CambiarRolAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.CambiarRolAsync(99, RolUsuario.Admin));
    }

    [Fact]
    public async Task CambiarContrasenaAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.CambiarContrasenaAsync(99, "nuevaContrasena123"));
    }
}
