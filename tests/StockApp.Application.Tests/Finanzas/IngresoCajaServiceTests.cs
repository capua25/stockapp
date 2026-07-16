using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class IngresoCajaServiceTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static (IngresoCajaService svc,
                    Mock<IIngresoCajaRepository> repoMock,
                    Mock<IFuenteFinanciamientoRepository> fuentesMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IIngresoCajaRepository>();
        var fuentes = new Mock<IFuenteFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });

        var svc = new IngresoCajaService(repo.Object, fuentes.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, fuentes, audit);
    }

    private static IngresoCaja IngresoValido() => new()
    {
        Fecha = Hoy, Concepto = "Partida mensual FIGM", FuenteFinanciamientoId = 1, Monto = 250000m,
    };

    [Fact]
    public async Task AltaAsync_ConceptoVacio_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var ingreso = IngresoValido();
        ingreso.Concepto = " ";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(ingreso));
    }

    [Fact]
    public async Task AltaAsync_MontoNoPositivo_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var ingreso = IngresoValido();
        ingreso.Monto = 0m;

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(ingreso));
    }

    [Fact]
    public async Task AltaAsync_FuenteInactiva_LanzaReglaDeNegocio()
    {
        var (svc, _, fuentes, _) = Crear();
        fuentes.Setup(f => f.ObtenerPorIdAsync(1))
            .ReturnsAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Vieja", Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(IngresoValido()));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaIngresoCaja()
    {
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.AgregarAsync(It.IsAny<IngresoCaja>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(IngresoValido());

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaIngresoCaja, "IngresoCaja", 5,
            It.Is<string>(d => d.Contains("Partida mensual FIGM"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((IngresoCaja?)null);
        var ingreso = IngresoValido();
        ingreso.Id = 99;

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.ModificarAsync(ingreso));
    }

    [Fact]
    public async Task ModificarAsync_CambiaConceptoYMonto_ActualizaYAudita()
    {
        var original = IngresoValido();
        original.Id = 1;
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);

        var editado = IngresoValido();
        editado.Id = 1;
        editado.Concepto = "Multas junio";
        editado.Monto = 12000m;
        await svc.ModificarAsync(editado);

        repo.Verify(r => r.ActualizarAsync(It.Is<IngresoCaja>(i =>
            i.Concepto == "Multas junio" && i.Monto == 12000m)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionIngresoCaja, "IngresoCaja", 1,
            It.Is<string>(d => d.Contains("Concepto") && d.Contains("Monto"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var original = IngresoValido();
        original.Id = 1;
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);

        var editado = IngresoValido();
        editado.Id = 1;
        await svc.ModificarAsync(editado);

        repo.Verify(r => r.ActualizarAsync(It.IsAny<IngresoCaja>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBaja()
    {
        var ingreso = IngresoValido();
        ingreso.Id = 2;
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(ingreso);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<IngresoCaja>(i => !i.Activo)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaIngresoCaja, "IngresoCaja", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactivo_LanzaReglaDeNegocio()
    {
        var ingreso = IngresoValido();
        ingreso.Id = 2;
        ingreso.Activo = false;
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(ingreso);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task ListarTodosAsync_DelegaAlRepo()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ListarTodosAsync()).ReturnsAsync(new List<IngresoCaja> { IngresoValido() });

        var result = await svc.ListarTodosAsync();

        Assert.Single(result);
    }
}
