using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class CategoriaServiceTests
{
    private static (CategoriaService svc,
                    Mock<ICategoriaRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<ICategoriaRepository>();
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

        var svc = new CategoriaService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaInvalidOperation()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Lácteos", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.AltaAsync(new Categoria { Nombre = "Lácteos" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaCategoria()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Lácteos", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Categoria>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new Categoria { Nombre = "Lácteos" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaCategoria,
            "Categoria", 5, It.Is<string>(d => d.Contains("Lácteos"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new Categoria { Id = 1, Nombre = "Bebidas", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Bebidas y Licores", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new Categoria { Id = 1, Nombre = "Bebidas y Licores", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<Categoria>(c => c.Nombre == "Bebidas y Licores")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionCategoria,
            "Categoria", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaCategoria()
    {
        var c = new Categoria { Id = 2, Nombre = "Carnes", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(c);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<Categoria>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaCategoria,
            "Categoria", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaInvalidOperation()
    {
        var c = new Categoria { Id = 2, Nombre = "Carnes", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(c);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new Categoria { Nombre = "Carnes" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Categoria>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new Categoria { Nombre = "Nuevas" }));

        Assert.Null(ex);
    }
}
