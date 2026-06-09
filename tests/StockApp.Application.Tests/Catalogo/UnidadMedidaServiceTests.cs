using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class UnidadMedidaServiceTests
{
    private static (UnidadMedidaService svc,
                    Mock<IUnidadMedidaRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IUnidadMedidaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesion = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesion);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarTablasMaestras))
                .Throws<UnauthorizedAccessException>();

        var svc = new UnidadMedidaService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaInvalidOperation()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Kilogramo", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" }));
    }

    [Fact]
    public async Task AltaAsync_AbrebiaturaDuplicada_LanzaInvalidOperation()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Kilogramo", null)).ReturnsAsync(false);
        repo.Setup(r => r.ExisteAbreviaturaAsync("kg", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaUnidadMedida()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Kilogramo", null)).ReturnsAsync(false);
        repo.Setup(r => r.ExisteAbreviaturaAsync("kg", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<UnidadMedida>())).ReturnsAsync(7);

        var id = await svc.AltaAsync(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" });

        Assert.Equal(7, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaUnidadMedida,
            "UnidadMedida", 7,
            It.Is<string>(d => d.Contains("Kilogramo") && d.Contains("kg"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new UnidadMedida { Id = 1, Nombre = "Kilo", Abreviatura = "k", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(false);
        repo.Setup(r => r.ExisteAbreviaturaAsync(It.IsAny<string>(), It.IsAny<int?>())).ReturnsAsync(false);

        await svc.ModificarAsync(new UnidadMedida { Id = 1, Nombre = "Kilogramo", Abreviatura = "kg", Activo = true });

        repo.Verify(r => r.ActualizarAsync(
            It.Is<UnidadMedida>(u => u.Nombre == "Kilogramo" && u.Abreviatura == "kg")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionUnidadMedida,
            "UnidadMedida", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaUnidadMedida()
    {
        var u = new UnidadMedida { Id = 4, Nombre = "Litro", Abreviatura = "l", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(u);

        await svc.BajaLogicaAsync(4);

        repo.Verify(r => r.ActualizarAsync(It.Is<UnidadMedida>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaUnidadMedida,
            "UnidadMedida", 4, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaInvalidOperation()
    {
        var u = new UnidadMedida { Id = 4, Nombre = "Litro", Abreviatura = "l", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(u);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(4));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new UnidadMedida { Nombre = "X", Abreviatura = "x" }));
    }
}
