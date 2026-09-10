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

public class OrigenFinanciamientoServiceTests
{
    private static (OrigenFinanciamientoService svc,
                    Mock<IOrigenFinanciamientoRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IOrigenFinanciamientoRepository>();
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

        var svc = new OrigenFinanciamientoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Presupuesto propio", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaOrigenFinanciamiento()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Presupuesto propio", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrigenFinanciamiento>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaOrigenFinanciamiento,
            "OrigenFinanciamiento", 5, It.Is<string>(d => d.Contains("Presupuesto propio"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new OrigenFinanciamiento { Id = 1, Nombre = "Fondo nacional", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Fondo nacional de infraestructura", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new OrigenFinanciamiento { Id = 1, Nombre = "Fondo nacional de infraestructura", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<OrigenFinanciamiento>(o => o.Nombre == "Fondo nacional de infraestructura")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionOrigenFinanciamiento,
            "OrigenFinanciamiento", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaOrigenFinanciamiento()
    {
        var o = new OrigenFinanciamiento { Id = 2, Nombre = "Convenio", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(o);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<OrigenFinanciamiento>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaOrigenFinanciamiento,
            "OrigenFinanciamiento", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var o = new OrigenFinanciamiento { Id = 2, Nombre = "Convenio", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(o);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Convenio" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrigenFinanciamiento>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Nuevo" }));

        Assert.Null(ex);
    }

    // ─── ListarActivasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>
        {
            new() { Id = 1, Nombre = "Presupuesto propio", Activo = true },
            new() { Id = 2, Nombre = "Discontinuado", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Presupuesto propio", activas[0].Nombre);
    }

    [Fact]
    public async Task ListarActivasAsync_Operador_NoLanzaUnauthorized()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonaServiceTests.
        var (svc, repo, _, _, _) = Crear(RolUsuario.Operador);
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>());

        var ex = await Record.ExceptionAsync(() => svc.ListarActivasAsync());

        Assert.Null(ex);
    }

    // ─── EntidadNoEncontradaException ───────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_OrigenInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((OrigenFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new OrigenFinanciamiento { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_OrigenInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((OrigenFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        var parametros = typeof(OrigenFinanciamientoService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(4, parametros.Length);
    }

    // ─── Normalización de nombre (Trim + case-insensitive) ──────────────────

    [Fact]
    public async Task AltaAsync_TrimmeaNombre_YConservaCasingDelUsuario()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Villa Nueva", null)).ReturnsAsync(false);
        OrigenFinanciamiento? capturado = null;
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrigenFinanciamiento>()))
            .Callback<OrigenFinanciamiento>(o => capturado = o)
            .ReturnsAsync(1);

        await svc.AltaAsync(new OrigenFinanciamiento { Nombre = "  Villa Nueva  " });

        Assert.Equal("Villa Nueva", capturado!.Nombre);
    }

    [Fact]
    public async Task AltaAsync_NombreSoloEspacios_LanzaArgumentException()
    {
        var (svc, _, _, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "   " }));
    }

    [Fact]
    public async Task ModificarAsync_SoloCambiaCasing_PersisteElNuevoCasing()
    {
        // Guardián contra el fallo silencioso: corregir "presupuesto propio" a "Presupuesto
        // propio" es un cambio legítimo del ABM y TIENE que persistirse. La comparación
        // case-insensitive es solo para decidir si choca con OTRA fila (ExisteNombreAsync ya
        // excluye la propia por Id).
        var original = new OrigenFinanciamiento { Id = 1, Nombre = "presupuesto propio", Activo = true };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Presupuesto propio", 1)).ReturnsAsync(false);

        var ex = await Record.ExceptionAsync(
            () => svc.ModificarAsync(new OrigenFinanciamiento { Id = 1, Nombre = "Presupuesto propio", Activo = true }));

        Assert.Null(ex);
        repo.Verify(r => r.ActualizarAsync(It.Is<OrigenFinanciamiento>(o => o.Nombre == "Presupuesto propio")), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_TrimmeaNombreEntrante()
    {
        var original = new OrigenFinanciamiento { Id = 1, Nombre = "Fondo nacional", Activo = true };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Fondo nacional de infraestructura", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new OrigenFinanciamiento { Id = 1, Nombre = "  Fondo nacional de infraestructura  ", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<OrigenFinanciamiento>(o => o.Nombre == "Fondo nacional de infraestructura")), Times.Once);
    }
}
