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
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth, Mock<IAuditLogger> Audit)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo     = new Mock<ITareaRepository>();
        var usuarios = new Mock<IUsuarioRepository>();
        var session  = new Mock<ICurrentSession>();
        var auth     = new Mock<IAuthorizationService>();
        var audit    = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new TareaService(repo.Object, usuarios.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, usuarios, session, auth, audit);
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
        var fechaInicioOriginal = DateTime.UtcNow.AddHours(-2);
        var tarea = new Tarea
        {
            Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso,
            TomadaPorUsuarioId = 1, FechaInicio = fechaInicioOriginal,
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.TerminarAsync(5);

        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
        Assert.Equal(1, tarea.CerradaPorUsuarioId);
        Assert.NotNull(tarea.FechaFin);

        // El par de trazabilidad de "toma" (quién y cuándo la agarró) debe sobrevivir
        // intacto al cierre: es lo que distingue quién la tomó de quién la cerró.
        Assert.Equal(1, tarea.TomadaPorUsuarioId);
        Assert.Equal(fechaInicioOriginal, tarea.FechaInicio);
    }

    [Fact]
    public async Task TerminarAsync_TareaPropia_NoGeneraNotaAutomatica()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 1 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.TerminarAsync(5);

        Assert.Empty(tarea.Notas);
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

    // ── CancelarAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelarAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.AdministrarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.CancelarAsync(1));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CancelarAsync_ComoAdmin_CambiaACanceladaYRegistraCierre()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.CancelarAsync(1);

        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
        Assert.Equal(1, tarea.CerradaPorUsuarioId);
    }

    // ── CambiarPrioridadAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CambiarPrioridadAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.AdministrarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Alta));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_ComoAdmin_CambiaLaPrioridadYGeneraNotaAutomatica()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 1, Titulo = "x", Prioridad = PrioridadTarea.Media };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Alta);

        Assert.Equal(PrioridadTarea.Alta, tarea.Prioridad);
        var nota = Assert.Single(tarea.Notas);
        Assert.Equal("Prioridad: Media → Alta", nota.Texto);
        Assert.True(nota.EsAutomatica);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_MismaPrioridad_NoHaceNadaYNoGeneraNota()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Prioridad = PrioridadTarea.Media };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Media);

        Assert.Empty(tarea.Notas);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    // ── AgregarNotaAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarNotaAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.AgregarNotaAsync(1, "avance"));
    }

    [Fact]
    public async Task AgregarNotaAsync_TextoVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.AgregarNotaAsync(1, "   "));
    }

    [Fact]
    public async Task AgregarNotaAsync_GuardaLaNotaConSuAutorYRegistraAuditoria()
    {
        var ctx = Crear(idSesion: 3);
        var tarea = new Tarea { Id = 1, Titulo = "x" };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.AgregarNotaAsync(1, "avance del trabajo");

        var nota = Assert.Single(tarea.Notas);
        Assert.Equal(3, nota.UsuarioId);
        Assert.Equal("avance del trabajo", nota.Texto);
        Assert.False(nota.EsAutomatica);
        ctx.Audit.Verify(a => a.RegistrarAsync(3, AccionAuditada.AltaNotaTarea, "Tarea", 1, It.IsAny<string>()), Times.Once);
    }

    // ── Notas append-only: sin métodos para editar ni borrar ─────────────────

    [Fact]
    public void ITareaService_ExponeExactamenteLosMetodosEsperados()
    {
        // Lista blanca intencionalmente cerrada (no lista negra de sinónimos): una lista
        // negra de "Editar/Borrar/Eliminar + Nota" solo atrapa los nombres que ya se te
        // ocurrieron (ActualizarNota, CorregirNota, QuitarNota, ArchivarNota... pasan
        // gratis). Con la lista blanca, CUALQUIER método nuevo en la interfaz —se llame
        // como se llame— rompe este test y obliga a quien lo agrega a justificarlo acá.
        // Así se protege el append-only de las notas (decisión 12 del spec: se agregan,
        // no se editan ni se borran).
        var esperados = new HashSet<string>
        {
            nameof(ITareaService.CrearAsync),
            nameof(ITareaService.ListarAsync),
            nameof(ITareaService.TomarAsync),
            nameof(ITareaService.SoltarAsync),
            nameof(ITareaService.TerminarAsync),
            nameof(ITareaService.CancelarAsync),
            nameof(ITareaService.CambiarPrioridadAsync),
            nameof(ITareaService.AgregarNotaAsync),
        };

        var metodos = typeof(ITareaService).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Equal(esperados, metodos);
    }

    // ── Auditoría en el resto de las acciones ─────────────────────────────────

    [Fact]
    public async Task CrearAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(9);

        await ctx.Svc.CrearAsync(new Tarea { Titulo = "Reparar bache" });

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.AltaTarea, "Tarea", 9, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task TomarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.TomarAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.CambioEstadoTarea, "Tarea", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_RegistraAuditoria()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.CancelarAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.CancelacionTarea, "Tarea", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_RegistraAuditoria()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Prioridad = PrioridadTarea.Media };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.CambiarPrioridadAsync(5, PrioridadTarea.Alta);

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.CambioPrioridadTarea, "Tarea", 5, It.IsAny<string>()), Times.Once);
    }
}
