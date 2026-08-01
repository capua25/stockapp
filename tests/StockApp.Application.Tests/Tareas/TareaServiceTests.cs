using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Tareas;

public class TareaServiceTests
{
    private static (TareaService Svc, Mock<ITareaRepository> Repo, Mock<IUsuarioRepository> Usuarios,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo     = new Mock<ITareaRepository>();
        var usuarios = new Mock<IUsuarioRepository>();
        var session  = new Mock<ICurrentSession>();
        var auth     = new Mock<IAuthorizationService>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new TareaService(repo.Object, usuarios.Object, session.Object, auth.Object);
        return (svc, repo, usuarios, session, auth);
    }

    // ── CrearAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "Reparar bache" }));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task CrearAsync_TituloVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.CrearAsync(new Tarea { Titulo = "  " }));
    }

    [Fact]
    public async Task CrearAsync_PrioridadSiempreMedia_AunSiLlegaOtraEnLaEntidad()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(1);
        var tarea = new Tarea { Titulo = "Reparar bache", Prioridad = PrioridadTarea.Alta };

        await ctx.Svc.CrearAsync(tarea);

        ctx.Repo.Verify(r => r.AgregarAsync(
            It.Is<Tarea>(t => t.Prioridad == PrioridadTarea.Media)), Times.Once);
    }

    [Fact]
    public async Task CrearAsync_DatosValidos_DelegaAlRepoYDevuelveId()
    {
        var ctx = Crear(idSesion: 7);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(42);

        var id = await ctx.Svc.CrearAsync(new Tarea { Titulo = "Reparar bache" });

        Assert.Equal(42, id);
        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<Tarea>(t =>
            t.Titulo == "Reparar bache" && t.CreadaPorUsuarioId == 7 && t.Estado == EstadoTarea.Pendiente)),
            Times.Once);
    }

    // ── ListarAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ListarAsync());
    }

    [Fact]
    public async Task ListarAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ListarAsync())
            .ReturnsAsync(new List<Tarea> { new() { Id = 1, Titulo = "x" } });

        var tareas = await ctx.Svc.ListarAsync();

        Assert.Single(tareas);
    }

    // ── TomarAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TomarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.TomarAsync(1));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task TomarAsync_TareaInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((Tarea?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.TomarAsync(1));
    }

    [Fact]
    public async Task TomarAsync_DesdePendiente_CambiaAEnCursoYRegistraResponsable()
    {
        var ctx = Crear(idSesion: 3);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.TomarAsync(1);

        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
        Assert.Equal(3, tarea.TomadaPorUsuarioId);
        Assert.NotNull(tarea.FechaInicio);
        ctx.Repo.Verify(r => r.ActualizarAsync(tarea), Times.Once);
    }

    [Fact]
    public async Task TomarAsync_DesdeEnCurso_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.EnCurso };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.TomarAsync(1));
    }

    // ── SoltarAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SoltarAsync_DesdeEnCurso_DejaLaTareaPendienteYLimpiaResponsable()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.EnCurso,
            TomadaPorUsuarioId = 1, FechaInicio = DateTime.UtcNow,
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.SoltarAsync(1);

        Assert.Equal(EstadoTarea.Pendiente, tarea.Estado);
        Assert.Null(tarea.TomadaPorUsuarioId);
        Assert.Null(tarea.FechaInicio);
    }

    [Fact]
    public async Task SoltarAsync_TareaAjena_GeneraNotaAutomaticaConNombres()
    {
        var ctx = Crear(idSesion: 1, nombreUsuario: "garcia");
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 99 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Usuarios.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync(new Usuario { Id = 99, NombreUsuario = "juan" });

        await ctx.Svc.SoltarAsync(5);

        var nota = Assert.Single(tarea.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal("garcia soltó una tarea tomada por juan.", nota.Texto);
    }

    [Fact]
    public async Task SoltarAsync_TareaPropia_NoGeneraNotaAutomatica()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 1 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.SoltarAsync(5);

        Assert.Empty(tarea.Notas);
    }

    // ── TerminarAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TerminarAsync_DesdeEnCurso_CambiaATerminadaYRegistraCierre()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 1 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.TerminarAsync(5);

        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
        Assert.Equal(1, tarea.CerradaPorUsuarioId);
        Assert.NotNull(tarea.FechaFin);
    }

    [Fact]
    public async Task TerminarAsync_TareaAjena_GeneraNotaAutomaticaConNombres()
    {
        var ctx = Crear(idSesion: 1, nombreUsuario: "garcia");
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 99 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Usuarios.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync(new Usuario { Id = 99, NombreUsuario = "juan" });

        await ctx.Svc.TerminarAsync(5);

        var nota = Assert.Single(tarea.Notas);
        Assert.Equal("garcia terminó una tarea tomada por juan.", nota.Texto);
    }
}
