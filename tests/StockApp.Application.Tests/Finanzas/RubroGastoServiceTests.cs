using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class RubroGastoServiceTests
{
    private static (RubroGastoService svc,
                    Mock<IRubroGastoRepository> repoMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IRubroGastoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        var svc = new RubroGastoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, audit);
    }

    [Fact]
    public async Task AltaAsync_CodigoNoPositivo_LanzaArgumentException()
    {
        var (svc, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new RubroGasto { Codigo = 0, Nombre = "Combustibles" }));
    }

    [Fact]
    public async Task AltaAsync_NombreVacio_LanzaArgumentException()
    {
        var (svc, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new RubroGasto { Codigo = 3, Nombre = " " }));
    }

    [Fact]
    public async Task AltaAsync_CodigoDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync(3, null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaRubroGasto()
    {
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync(3, null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<RubroGasto>())).ReturnsAsync(7);

        var id = await svc.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" });

        Assert.Equal(7, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaRubroGasto,
            "RubroGasto", 7, It.Is<string>(d => d.Contains("Combustibles") && d.Contains("3"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_CambiaCodigoYNombre_ActualizaYAudita()
    {
        var original = new RubroGasto { Id = 1, Codigo = 3, Nombre = "Combustible", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteCodigoAsync(4, 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new RubroGasto { Id = 1, Codigo = 4, Nombre = "Combustibles y Lubricantes" });

        repo.Verify(r => r.ActualizarAsync(
            It.Is<RubroGasto>(x => x.Codigo == 4 && x.Nombre == "Combustibles y Lubricantes")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionRubroGasto,
            "RubroGasto", 1, It.Is<string>(d => d.Contains("Código") && d.Contains("Nombre"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((RubroGasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new RubroGasto { Id = 99, Codigo = 1, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaRubroGasto()
    {
        var rubro = new RubroGasto { Id = 2, Codigo = 5, Nombre = "Papelería", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(rubro);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<RubroGasto>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaRubroGasto,
            "RubroGasto", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactivo_LanzaReglaDeNegocio()
    {
        var rubro = new RubroGasto { Id = 2, Codigo = 5, Nombre = "Papelería", Activo = false };
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(rubro);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraInactivos()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ListarTodosAsync()).ReturnsAsync(new List<RubroGasto>
        {
            new() { Id = 1, Codigo = 1, Nombre = "Sueldos", Activo = true },
            new() { Id = 2, Codigo = 2, Nombre = "Viejo", Activo = false },
        });

        var activos = await svc.ListarActivosAsync();

        Assert.Single(activos);
        Assert.Equal("Sueldos", activos[0].Nombre);
    }
}
