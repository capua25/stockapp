using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class LineaPoaServiceTests
{
    private static (LineaPoaService svc,
                    Mock<ILineaPoaRepository> repoMock,
                    Mock<IFuenteFinanciamientoRepository> fuentesMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<ILineaPoaRepository>();
        var fuentes = new Mock<IFuenteFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        // Por defecto, cualquier fuente consultada existe (los tests de fuente
        // inexistente lo pisan).
        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });

        var svc = new LineaPoaService(repo.Object, fuentes.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, fuentes, audit);
    }

    private static LineaPoa LineaValida() => new()
    {
        Nombre = "COMPOSTERAS",
        Programa = "Ambiente",
        Ejercicio = 2026,
        Asignaciones =
        {
            new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 100000m },
            new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 50000m },
        },
    };

    [Fact]
    public async Task AltaAsync_NombreVacio_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Nombre = " ";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_ProgramaVacio_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Programa = "";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_EjercicioNoPositivo_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Ejercicio = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_SinAsignaciones_LanzaReglaDeNegocio()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Asignaciones.Clear();

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(linea));
        Assert.Contains("al menos una asignación", ex.Message);
    }

    [Fact]
    public async Task AltaAsync_MontoNoPositivo_LanzaReglaDeNegocio()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Asignaciones[0].Monto = 0m;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_FuenteRepetida_LanzaReglaDeNegocio()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Asignaciones[1].FuenteFinanciamientoId = linea.Asignaciones[0].FuenteFinanciamientoId;

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(linea));
        Assert.Contains("repetida", ex.Message);
    }

    [Fact]
    public async Task AltaAsync_FuenteInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, _, fuentes, _) = Crear();
        fuentes.Setup(f => f.ObtenerPorIdAsync(2)).ReturnsAsync((FuenteFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.AltaAsync(LineaValida()));
    }

    [Fact]
    public async Task AltaAsync_NombreYEjercicioDuplicados_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreEjercicioAsync("COMPOSTERAS", 2026, null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(LineaValida()));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaLineaPoa()
    {
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreEjercicioAsync("COMPOSTERAS", 2026, null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<LineaPoa>())).ReturnsAsync(4);

        var id = await svc.AltaAsync(LineaValida());

        Assert.Equal(4, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaLineaPoa,
            "LineaPoa", 4, It.Is<string>(d => d.Contains("COMPOSTERAS") && d.Contains("2026"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((LineaPoa?)null);

        var linea = LineaValida();
        linea.Id = 99;

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.ModificarAsync(linea));
    }

    [Fact]
    public async Task ModificarAsync_CambiaCamposYAsignaciones_ActualizaYAudita()
    {
        var original = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 1, Monto = 100000m } },
        };
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreEjercicioAsync("COMPOSTERAS II", 2026, 4)).ReturnsAsync(false);

        var editada = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS II", Programa = "Ambiente", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 80000m } },
        };
        await svc.ModificarAsync(editada);

        repo.Verify(r => r.ActualizarAsync(
            It.Is<LineaPoa>(l => l.Id == 4 && l.Nombre == "COMPOSTERAS II"),
            It.Is<IReadOnlyList<AsignacionPresupuestal>>(a =>
                a.Count == 1 && a[0].FuenteFinanciamientoId == 2 && a[0].Monto == 80000m)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionLineaPoa,
            "LineaPoa", 4, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var original = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 1, Monto = 100000m } },
        };
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(original);

        var igual = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 100000m } },
        };
        await svc.ModificarAsync(igual);

        repo.Verify(r => r.ActualizarAsync(It.IsAny<LineaPoa>(), It.IsAny<IReadOnlyList<AsignacionPresupuestal>>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaLineaPoa()
    {
        var linea = new LineaPoa { Id = 3, Nombre = "PRENSA", Programa = "Comunicación", Ejercicio = 2026, Activo = true };
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(linea);

        await svc.BajaLogicaAsync(3);

        repo.Verify(r => r.ActualizarAsync(
            It.Is<LineaPoa>(l => l.Activo == false),
            It.Is<IReadOnlyList<AsignacionPresupuestal>>(a => a.Count == 0)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaLineaPoa,
            "LineaPoa", 3, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var linea = new LineaPoa { Id = 3, Nombre = "PRENSA", Programa = "Comunicación", Ejercicio = 2026, Activo = false };
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(linea);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(3));
    }

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026, Activo = true },
            new() { Id = 2, Nombre = "Vieja", Programa = "Obras", Ejercicio = 2025, Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Rambla", activas[0].Nombre);
    }
}
