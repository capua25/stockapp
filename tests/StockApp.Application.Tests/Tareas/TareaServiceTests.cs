using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Tareas;

public class TareaServiceTests
{
    private static (TareaService Svc, Mock<ITareaRepository> Repo,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo    = new Mock<ITareaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthorizationService>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new TareaService(repo.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
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
}
