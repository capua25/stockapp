using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class FuenteFinanciamientoServiceTests
{
    private static (FuenteFinanciamientoService svc,
                    Mock<IFuenteFinanciamientoRepository> repoMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IFuenteFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        var svc = new FuenteFinanciamientoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, audit);
    }

    [Fact]
    public async Task AltaAsync_NombreVacio_LanzaArgumentException()
    {
        var (svc, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new FuenteFinanciamiento { Nombre = "  " }));
    }

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Literal B", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new FuenteFinanciamiento { Nombre = "Literal B" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaFuenteFinanciamiento()
    {
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Multas", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<FuenteFinanciamiento>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new FuenteFinanciamiento { Nombre = "Multas" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaFuenteFinanciamiento,
            "FuenteFinanciamiento", 5, It.Is<string>(d => d.Contains("Multas"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((FuenteFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new FuenteFinanciamiento { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task ModificarAsync_CambiaNombre_ActualizaYAudita()
    {
        var original = new FuenteFinanciamiento { Id = 1, Nombre = "Literal A", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Literal A (FIGM)", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Literal A (FIGM)" });

        repo.Verify(r => r.ActualizarAsync(It.Is<FuenteFinanciamiento>(f => f.Nombre == "Literal A (FIGM)")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionFuenteFinanciamiento,
            "FuenteFinanciamiento", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var original = new FuenteFinanciamiento { Id = 1, Nombre = "Literal A", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);

        await svc.ModificarAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Literal A" });

        repo.Verify(r => r.ActualizarAsync(It.IsAny<FuenteFinanciamiento>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBaja()
    {
        var fuente = new FuenteFinanciamiento { Id = 2, Nombre = "Multas", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(fuente);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<FuenteFinanciamiento>(f => f.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaFuenteFinanciamiento,
            "FuenteFinanciamiento", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var fuente = new FuenteFinanciamiento { Id = 2, Nombre = "Multas", Activo = false };
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(fuente);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Literal B", Activo = true },
            new() { Id = 2, Nombre = "Vieja", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Literal B", activas[0].Nombre);
    }
}
