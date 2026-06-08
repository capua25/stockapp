using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Auth;

public class AuthServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Usuario UsuarioActivo(RolUsuario rol = RolUsuario.Operador) => new()
    {
        Id = 1,
        NombreUsuario = "usuario1",
        HashContrasena = "$2a$12$hash_valido",   // simulado
        Rol = rol,
        Activo = true,
        FechaAlta = DateTime.UtcNow
    };

    private static (AuthService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuditLogger> auditMock)
        Crear(Usuario? usuarioEnBd = null, bool hashValido = true)
    {
        var repo = new Mock<IUsuarioRepository>();
        repo.Setup(r => r.BuscarPorNombreAsync("usuario1"))
            .ReturnsAsync(usuarioEnBd);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
              .Returns(hashValido);

        var session = new Mock<ICurrentSession>();
        var audit   = new Mock<IAuditLogger>();

        var svc = new AuthService(repo.Object, hasher.Object, session.Object, audit.Object);
        return (svc, repo, hasher, session, audit);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Correcto_EstableceSesionYActualizaUltimoAcceso()
    {
        var usuario = UsuarioActivo();
        var (svc, repo, _, session, _) = Crear(usuario, hashValido: true);

        var resultado = await svc.LoginAsync("usuario1", "contrasena");

        Assert.True(resultado.Exitoso);
        session.Verify(s => s.IniciarSesion(usuario), Times.Once);
        repo.Verify(r => r.ActualizarUltimoAccesoAsync(usuario.Id, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Login_ContrasenaIncorrecta_FallaYNoEstableceSesion()
    {
        var usuario = UsuarioActivo();
        var (svc, _, _, session, _) = Crear(usuario, hashValido: false);

        var resultado = await svc.LoginAsync("usuario1", "mala");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.ContrasenaInvalida, resultado.Error);
        session.Verify(s => s.IniciarSesion(It.IsAny<Usuario>()), Times.Never);
    }

    [Fact]
    public async Task Login_UsuarioInexistente_FallaConErrorAdecuado()
    {
        var (svc, _, _, session, _) = Crear(usuarioEnBd: null);

        var resultado = await svc.LoginAsync("noexiste", "cualquiera");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.UsuarioNoEncontrado, resultado.Error);
        session.Verify(s => s.IniciarSesion(It.IsAny<Usuario>()), Times.Never);
    }

    [Fact]
    public async Task Login_UsuarioInactivo_FallaConErrorEspecifico()
    {
        var usuario = UsuarioActivo();
        usuario.Activo = false;
        var (svc, _, _, session, _) = Crear(usuario, hashValido: true);

        var resultado = await svc.LoginAsync("usuario1", "contrasena");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.UsuarioInactivo, resultado.Error);
        session.Verify(s => s.IniciarSesion(It.IsAny<Usuario>()), Times.Never);
    }

    [Fact]
    public async Task Logout_LlamaCerrarSesion()
    {
        var (svc, _, _, session, _) = Crear();

        await svc.LogoutAsync();

        session.Verify(s => s.CerrarSesion(), Times.Once);
    }
}
