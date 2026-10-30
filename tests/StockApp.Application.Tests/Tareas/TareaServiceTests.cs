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
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth, Mock<IAuditLogger> Audit,
                     Mock<IDocumentoAdministrativoRepository> Documentos,
                     Mock<IProyectoRepository> Proyectos)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo        = new Mock<ITareaRepository>();
        var usuarios    = new Mock<IUsuarioRepository>();
        var session     = new Mock<ICurrentSession>();
        var auth        = new Mock<IAuthorizationService>();
        var audit       = new Mock<IAuditLogger>();
        var documentos  = new Mock<IDocumentoAdministrativoRepository>();
        var proyectos   = new Mock<IProyectoRepository>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        // Fix (review final, Critical): NotaAjenaAsync ahora resuelve el nombre del actor
        // contra IUsuarioRepository (igual que el tomador) en vez de leerlo de la sesión —
        // HttpCurrentSession (el ICurrentSession real de la API) siempre lo trae vacío. Este
        // mock del actor mantiene "garcia"/"admin.test" disponibles para los tests de nota
        // automática que ya existían, sin cambiar ninguna aserción.
        usuarios.Setup(u => u.ObtenerPorIdAsync(idSesion))
            .ReturnsAsync(new Usuario { Id = idSesion, NombreUsuario = nombreUsuario });

        var svc = new TareaService(
            repo.Object, usuarios.Object, session.Object, auth.Object, audit.Object,
            documentos.Object, proyectos.Object);
        return (svc, repo, usuarios, session, auth, audit, documentos, proyectos);
    }

    // ── CrearAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
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
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
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
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
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
        ctx.Auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarTareas))
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

    [Fact]
    public async Task CancelarAsync_TareaAjena_GeneraNotaAutomaticaConNombres()
    {
        // Fix (review final, Important): antes solo Soltar y Terminar generaban nota
        // automática al actuar sobre una tarea ajena; Cancelar cancela trabajo en curso de
        // otro usuario (EnCurso→Cancelada es transición válida) y no dejaba rastro.
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1, nombreUsuario: "garcia");
        var tarea = new Tarea
        {
            Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 99,
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Usuarios.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync(new Usuario { Id = 99, NombreUsuario = "juan" });

        await ctx.Svc.CancelarAsync(5);

        var nota = Assert.Single(tarea.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal("garcia canceló una tarea tomada por juan.", nota.Texto);
    }

    [Fact]
    public async Task CancelarAsync_TareaPropiaOSinTomador_NoGeneraNotaAutomatica()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 1 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.CancelarAsync(5);

        Assert.Empty(tarea.Notas);
    }

    // ── CambiarPrioridadAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CambiarPrioridadAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarTareas))
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

    [Fact]
    public async Task CambiarPrioridadAsync_TareaTerminada_LanzaReglaDeNegocioSinTocarElRepo()
    {
        // Decisión 14 del spec: una vez Terminada o Cancelada, la prioridad no se puede
        // volver a tocar. La regla vive en Tarea.CambiarPrioridad; acá solo se verifica que
        // el servicio la deje propagar y no persista nada.
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada, Prioridad = PrioridadTarea.Media,
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Alta));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_TareaTerminadaConMismaPrioridad_CasoBordeDeliberado_NoHaceNadaSinLanzar()
    {
        // Caso borde deliberado (ver "Decisión: misma prioridad sobre tarea terminada" en
        // .superpowers/sdd/2026-08-01-modulo-tareas/fix-prioridad-terminada.md): el
        // early-return por igualdad corre ANTES que la validación de estado terminal de
        // Tarea.CambiarPrioridad, así que pedir la MISMA prioridad sobre una tarea cerrada es
        // un no-op silencioso (200), no un 409. Si el día de mañana alguien reordena los
        // checks de este método, este test es el que se entera.
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada, Prioridad = PrioridadTarea.Media,
        };
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
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
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
            nameof(ITareaService.ObtenerPorIdAsync),
            nameof(ITareaService.ReclasificarAsync),
            nameof(ITareaService.ListarPorDocumentoAsync),
            nameof(ITareaService.ListarPorProyectoAsync),
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

    // ── CrearAsync: validación del expediente (D12 del spec) ──────────────────

    private static DocumentoAdministrativo UnExpediente(int id, string numero = "0001") => new()
    {
        Id = id, Numero = numero, Anio = 2026, Tipo = TipoDocumento.Expediente,
        Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
        Estado = EstadoDocumento.EnProceso,
    };

    [Fact]
    public async Task CrearAsync_DocumentoNoEsExpediente_LanzaReglaDeNegocio()
    {
        // D12: el vínculo acepta SOLO documentos de tipo Expediente.
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 3, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Oficio,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.Pendiente,
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", DocumentoAdministrativoId = 3 }));
    }

    [Fact]
    public async Task CrearAsync_DocumentoExpedienteCerrado_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 3, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.Finalizado,
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", DocumentoAdministrativoId = 3 }));
    }

    [Fact]
    public async Task CrearAsync_ClasificacionCompletaYValida_DelegaAlRepoConProyectoYExpediente()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(5)).ReturnsAsync(UnExpediente(5));
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(2)).ReturnsAsync(UnProyecto(2, EstadoProyecto.Pendiente));
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(10);

        await ctx.Svc.CrearAsync(new Tarea { Titulo = "x", ProyectoId = 2, DocumentoAdministrativoId = 5 });

        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<Tarea>(t =>
            t.ProyectoId == 2 && t.DocumentoAdministrativoId == 5)), Times.Once);
    }

    [Fact]
    public async Task CrearAsync_SinClasificacion_NoConsultaLosRepositorios()
    {
        // Caso mayoritario (D7: ambos son opcionales) — no debería pagar consultas de
        // validación cuando el operador no clasificó nada.
        var ctx = Crear();
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(1);

        await ctx.Svc.CrearAsync(new Tarea { Titulo = "x" });

        ctx.Documentos.Verify(d => d.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
        ctx.Proyectos.Verify(p => p.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    // ── ObtenerPorIdAsync (spec 2026-09-08 — corrección de contrato) ────────────

    [Fact]
    public async Task ObtenerPorIdAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ObtenerPorIdAsync(1));
    }

    [Fact]
    public async Task ObtenerPorIdAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 5, Titulo = "x" };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        var resultado = await ctx.Svc.ObtenerPorIdAsync(5);

        Assert.Same(tarea, resultado);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_TareaInexistente_DevuelveNull()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(999)).ReturnsAsync((Tarea?)null);

        var resultado = await ctx.Svc.ObtenerPorIdAsync(999);

        Assert.Null(resultado);
    }

    // ── ReclasificarAsync (spec 2026-09-08) ─────────────────────────────────────

    [Fact]
    public async Task ReclasificarAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null)));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_TareaInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((Tarea?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null)));
    }

    [Fact]
    public async Task ReclasificarAsync_DocumentoNoEsExpediente_LanzaReglaDeNegocio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(4)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 4, Numero = "0002", Anio = 2026, Tipo = TipoDocumento.Suministro,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.Pendiente,
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, 4)));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_SinCambios_NoGeneraNotaNiAuditoriaNiPersiste()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            DocumentoAdministrativoId = 3, DocumentoAdministrativo = UnExpediente(3),
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(UnExpediente(3));

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, 3));

        Assert.Empty(tarea.Notas);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ReclasificacionTarea, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_TareaTerminada_AplicaLaClasificacionYNoCambiaElEstado()
    {
        // D9 del spec: la reclasificación alcanza también a tareas terminales, y NO las reabre.
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(UnExpediente(3));

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, 3));

        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
        Assert.Equal(3, tarea.DocumentoAdministrativoId);
    }

    [Fact]
    public async Task ReclasificarAsync_CambiaExpedienteYProyecto_GeneraNotaAutomaticaConElDiffLegible()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            ProyectoId = 8, Proyecto = new Proyecto { Id = 8, Titulo = "Bacheo" },
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(UnExpediente(3, "0099"));
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(6)).ReturnsAsync(UnProyecto(6, EstadoProyecto.EnCurso));

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(6, 3));

        var nota = Assert.Single(tarea.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal(
            "admin reclasificó — Expediente: (sin asignar) → 0099/2026; Proyecto: Bacheo → Plaza",
            nota.Texto);
        ctx.Repo.Verify(r => r.ActualizarAsync(tarea), Times.Once);
    }

    [Fact]
    public async Task ReclasificarAsync_DesasignaElExpediente_QuedaEnNullYSeRegistra()
    {
        // D21: null es una desasignación explícita, no "no tocar".
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            DocumentoAdministrativoId = 3, DocumentoAdministrativo = UnExpediente(3, "0099"),
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null));

        Assert.Null(tarea.DocumentoAdministrativoId);
        var nota = Assert.Single(tarea.Notas);
        Assert.Equal("admin reclasificó — Expediente: 0099/2026 → (sin asignar)", nota.Texto);
    }

    [Fact]
    public async Task ReclasificarAsync_RegistraAuditoriaConElMismoTextoDeLaNota()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(1)).ReturnsAsync(UnExpediente(1, "0099"));

        await ctx.Svc.ReclasificarAsync(5, new DatosClasificacionTarea(null, 1));

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.ReclasificacionTarea, "Tarea", 5,
            "Expediente: (sin asignar) → 0099/2026"),
            Times.Once);
    }

    // ── ListarPorDocumentoAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarPorDocumentoAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ListarPorDocumentoAsync(1));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ListarPorDocumentoAsync(7))
            .ReturnsAsync(new List<Tarea> { new() { Id = 1, Titulo = "x", DocumentoAdministrativoId = 7 } });

        var tareas = await ctx.Svc.ListarPorDocumentoAsync(7);

        Assert.Single(tareas);
    }

    // ── Proyecto (spec 2026-10-30) ────────────────────────────────────────────

    private static Proyecto UnProyecto(int id, EstadoProyecto estado) =>
        new() { Id = id, Titulo = "Plaza", Estado = estado };

    [Fact]
    public async Task CrearAsync_ConProyectoEnCurso_Vincula()
    {
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(5)).ReturnsAsync(UnProyecto(5, EstadoProyecto.EnCurso));
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(1);

        await ctx.Svc.CrearAsync(new Tarea { Titulo = "x", ProyectoId = 5 });

        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<Tarea>(t => t.ProyectoId == 5)), Times.Once);
    }

    [Fact]
    public async Task CrearAsync_ConProyectoTerminado_Lanza()
    {
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(5)).ReturnsAsync(UnProyecto(5, EstadoProyecto.Terminado));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", ProyectoId = 5 }));

        Assert.Equal("Solo se pueden vincular proyectos pendientes o en curso.", ex.Message);
        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task CrearAsync_ConProyectoInexistente_Lanza()
    {
        var ctx = Crear();
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(9)).ReturnsAsync((Proyecto?)null);

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", ProyectoId = 9 }));

        Assert.Equal("Proyecto 9 no encontrado.", ex.Message);
    }

    [Fact]
    public async Task ReclasificarAsync_MismoProyectoCerrado_Permitido()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente, ProyectoId = 5 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(5)).ReturnsAsync(UnProyecto(5, EstadoProyecto.Cancelado));
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(UnExpediente(3));

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(5, 3));

        Assert.Equal(5, tarea.ProyectoId);
        Assert.Equal(3, tarea.DocumentoAdministrativoId);
        ctx.Repo.Verify(r => r.ActualizarAsync(tarea), Times.Once);
    }

    [Fact]
    public async Task ReclasificarAsync_QuitarProyecto_DejaNull()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente, ProyectoId = 5,
                            Proyecto = UnProyecto(5, EstadoProyecto.EnCurso) };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null));

        Assert.Null(tarea.ProyectoId);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ReclasificacionTarea, "Tarea", 1,
            It.Is<string>(d => d.Contains("Proyecto: Plaza → (sin asignar)"))), Times.Once);
        ctx.Repo.Verify(r => r.ActualizarAsync(tarea), Times.Once);
    }

    [Fact]
    public async Task ListarPorProyectoAsync_SoloLasDelProyecto()
    {
        var ctx = Crear();
        var tareas = new List<Tarea> { new() { Id = 1, Titulo = "a", ProyectoId = 5 } };
        ctx.Repo.Setup(r => r.ListarPorProyectoAsync(5)).ReturnsAsync(tareas);

        var res = await ctx.Svc.ListarPorProyectoAsync(5);

        Assert.Same(tareas, res);
        ctx.Auth.Verify(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ReclasificarAsync_NuevoProyectoTerminado_Lanza()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(5)).ReturnsAsync(UnProyecto(5, EstadoProyecto.Terminado));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(5, null)));

        Assert.Equal("Solo se pueden vincular proyectos pendientes o en curso.", ex.Message);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_NuevoProyectoInexistente_Lanza()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Proyectos.Setup(p => p.ObtenerPorIdAsync(9)).ReturnsAsync((Proyecto?)null);

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(9, null)));

        Assert.Equal("Proyecto 9 no encontrado.", ex.Message);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }
}
