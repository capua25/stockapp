using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Reportes;

public class ReporteProyectosServiceTests
{
    // ── helpers de setup ──────────────────────────────────────────────────────

    private static (ReporteProyectosService svc,
                    Mock<IReporteProyectosRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IReporteProyectosRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);

        // Por defecto auth no lanza (permiso concedido)
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        var svc = new ReporteProyectosService(repo.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
    }

    private static FiltroReporteProyectos FiltroValido(
        AgrupadorProyectos agrupador = AgrupadorProyectos.Zona,
        CriterioFechaProyectos criterio = CriterioFechaProyectos.Creacion) =>
        new(agrupador, criterio, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

    private static FilaReporteProyectos Fila(
        string clasificador, int pendientes = 0, int enCurso = 0, int terminados = 0, int cancelados = 0) =>
        new(clasificador, pendientes, enCurso, terminados, cancelados,
            pendientes + enCurso + terminados + cancelados);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerAsync_Operador_LanzaUnauthorized_YNuncaLlamaAlRepo()
    {
        var (svc, repo, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerAsync(FiltroValido()));

        // Fail-closed: el repo NUNCA debe ser invocado.
        repo.Verify(r => r.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerAsync_RangoAusente_LanzaArgumentException()
    {
        // D18: el rango de fechas es obligatorio y se valida ACÁ, no solo en la UI.
        var (svc, repo, _, _) = Crear();
        var filtroSinRango = new FiltroReporteProyectos(
            AgrupadorProyectos.Zona, CriterioFechaProyectos.Creacion, default, default);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ObtenerAsync(filtroSinRango));

        repo.Verify(r => r.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerAsync_RangoInvertido_LanzaArgumentException()
    {
        var (svc, repo, _, _) = Crear();
        var filtroInvertido = new FiltroReporteProyectos(
            AgrupadorProyectos.Zona, CriterioFechaProyectos.Creacion,
            new DateTime(2026, 12, 31), new DateTime(2026, 1, 1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ObtenerAsync(filtroInvertido));

        repo.Verify(r => r.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerAsync_DelegaElFiltroExactoAlRepo()
    {
        var (svc, repo, _, _) = Crear();
        var filtro = FiltroValido(AgrupadorProyectos.OrigenFinanciamiento, CriterioFechaProyectos.Cierre);
        repo.Setup(r => r.ObtenerAsync(filtro)).ReturnsAsync(Array.Empty<FilaReporteProyectos>());

        await svc.ObtenerAsync(filtro);

        repo.Verify(r => r.ObtenerAsync(filtro), Times.Once);
    }

    [Fact]
    public async Task ObtenerAsync_CalculaTotalGeneral_SumandoLasFilasYaAgregadasPorElRepo()
    {
        // D23: el ÚNICO cálculo del reporte pasa por acá, sumando una lista ya agregada
        // por el repo (mismo criterio que ReporteStockService.ObtenerValorizacionAsync).
        var (svc, repo, _, _) = Crear();
        var filas = new[]
        {
            Fila("Centro", pendientes: 2, enCurso: 1, terminados: 3, cancelados: 0),
            Fila("Este", pendientes: 0, enCurso: 0, terminados: 1, cancelados: 1),
            Fila("(sin asignar)", pendientes: 1, enCurso: 0, terminados: 0, cancelados: 0),
        };
        repo.Setup(r => r.ObtenerAsync(It.IsAny<FiltroReporteProyectos>())).ReturnsAsync(filas);

        var resultado = await svc.ObtenerAsync(FiltroValido());

        Assert.Equal(3, resultado.Filas.Count);
        Assert.Same(filas, resultado.Filas);
        Assert.Equal("Total general", resultado.TotalGeneral.Clasificador);
        Assert.Equal(3, resultado.TotalGeneral.Pendientes);
        Assert.Equal(1, resultado.TotalGeneral.EnCurso);
        Assert.Equal(4, resultado.TotalGeneral.Terminados);
        Assert.Equal(1, resultado.TotalGeneral.Cancelados);
        Assert.Equal(6 + 2 + 1, resultado.TotalGeneral.Total); // 9
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        // Guardián estructural (D14 del spec 2026-09-08): a diferencia de ReporteStockService,
        // ReporteProyectosService no depende de IVersionReportes porque el reporte de proyectos no
        // se cachea. Si alguien "arregla" esto copiando de más desde ReporteStockService, este
        // test rompe apenas se agregue el parámetro al constructor.
        var parametros = typeof(ReporteProyectosService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(3, parametros.Length);
    }
}
