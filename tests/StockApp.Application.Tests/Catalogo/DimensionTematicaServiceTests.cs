using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class DimensionTematicaServiceTests
{
    private static (DimensionTematicaService svc,
                    Mock<IDimensionTematicaRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IDimensionTematicaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesion = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesion);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarTablasMaestras))
                .Throws<UnauthorizedAccessException>();

        var svc = new DimensionTematicaService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Infraestructura", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaDimensionTematica()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Infraestructura", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<DimensionTematica>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaDimensionTematica,
            "DimensionTematica", 5, It.Is<string>(d => d.Contains("Infraestructura"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new DimensionTematica { Id = 1, Nombre = "Tránsito", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Tránsito y Movilidad", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new DimensionTematica { Id = 1, Nombre = "Tránsito y Movilidad", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<DimensionTematica>(d => d.Nombre == "Tránsito y Movilidad")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionDimensionTematica,
            "DimensionTematica", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaDimensionTematica()
    {
        var d = new DimensionTematica { Id = 2, Nombre = "Ambiente", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(d);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<DimensionTematica>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaDimensionTematica,
            "DimensionTematica", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var d = new DimensionTematica { Id = 2, Nombre = "Ambiente", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(d);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new DimensionTematica { Nombre = "Ambiente" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<DimensionTematica>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new DimensionTematica { Nombre = "Nueva" }));

        Assert.Null(ex);
    }

    // ─── ListarActivasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<DimensionTematica>
        {
            new() { Id = 1, Nombre = "Infraestructura", Activo = true },
            new() { Id = 2, Nombre = "Discontinuada", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Infraestructura", activas[0].Nombre);
    }

    [Fact]
    public async Task ListarActivasAsync_Operador_NoLanzaUnauthorized()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonaServiceTests.
        var (svc, repo, _, _, _) = Crear(RolUsuario.Operador);
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<DimensionTematica>());

        var ex = await Record.ExceptionAsync(() => svc.ListarActivasAsync());

        Assert.Null(ex);
    }

    // ─── EntidadNoEncontradaException ───────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_DimensionInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((DimensionTematica?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new DimensionTematica { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_DimensionInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((DimensionTematica?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        var parametros = typeof(DimensionTematicaService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(4, parametros.Length);
    }
}
