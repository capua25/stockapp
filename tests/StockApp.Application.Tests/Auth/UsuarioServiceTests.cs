using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Auth;

public class UsuarioServiceTests
{
    private static (UsuarioService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rolSesion = RolUsuario.Admin)
    {
        var repo    = new Mock<IUsuarioRepository>();
        var hasher  = new Mock<IPasswordHasher>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rolSesion);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        // Admin: Verificar no lanza
        if (rolSesion == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarUsuarios))
                .Throws<UnauthorizedAccessException>();

        var svc = new UsuarioService(repo.Object, hasher.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, hasher, session, auth, audit);
    }

    [Fact]
    public async Task AltaUsuario_Admin_CreaConHashYEventoAuditoria()
    {
        var (svc, repo, hasher, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });

        await svc.AltaUsuarioAsync("operador2", "Nombre Completo", "pwd123", RolUsuario.Operador);

        hasher.Verify(h => h.Hash("pwd123"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "operador2" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Operador &&
            u.Activo == true
        )), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaUsuario,
            "Usuario", It.IsAny<int>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogica_Admin_PoneActivoFalseYNoEliminaRegistro()
    {
        var usuario = new Usuario
        {
            Id = 5, NombreUsuario = "operador1", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(usuario);

        await svc.BajaLogicaAsync(5);

        // Se actualizó, NO se eliminó
        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaUsuario,
            "Usuario", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AltaUsuario_Operador_LanzaUnauthorized()
    {
        var (svc, _, _, _, _, _) = Crear(rolSesion: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaUsuarioAsync("x", null, "pwd", RolUsuario.Operador));
    }

    [Fact]
    public async Task CambioRol_Admin_ActualizaYRegistraAuditoria()
    {
        var usuario = new Usuario
        {
            Id = 3, NombreUsuario = "alguien", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(usuario);

        await svc.CambiarRolAsync(3, RolUsuario.Admin);

        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Rol == RolUsuario.Admin)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioRol, "Usuario", 3, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambioContrasena_Admin_HashYRegistraAuditoria()
    {
        var usuario = new Usuario
        {
            Id = 4, NombreUsuario = "alguien", HashContrasena = "hash_viejo",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, hasher, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(usuario);

        await svc.CambiarContrasenaAsync(4, "nuevaContrasena");

        hasher.Verify(h => h.Hash("nuevaContrasena"), Times.Once);
        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u =>
            u.HashContrasena == "$2a$12$hashed")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioContrasena, "Usuario", 4, It.IsAny<string>()), Times.Once);
    }
}
