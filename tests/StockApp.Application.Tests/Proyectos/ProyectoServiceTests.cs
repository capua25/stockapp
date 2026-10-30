using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Proyectos;

public class ProyectoServiceTests
{
    private record Ctx(
        ProyectoService Svc, Mock<IProyectoRepository> Repo, Mock<ICurrentSession> Session,
        Mock<IAuditLogger> Audit, Mock<IZonaRepository> Zonas, Mock<IDimensionTematicaRepository> Dimensiones,
        Mock<IOrganismoResponsableRepository> Organismos, Mock<IOrigenFinanciamientoRepository> Origenes);

    /// <summary>Usa el AuthorizationService real: la matriz de permisos prueba la regla de verdad.</summary>
    private static Ctx Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1,
        IReadOnlySet<string>? permisos = null, bool autenticado = true)
    {
        var repo        = new Mock<IProyectoRepository>();
        var session     = new Mock<ICurrentSession>();
        var audit       = new Mock<IAuditLogger>();
        var zonas       = new Mock<IZonaRepository>();
        var dimensiones = new Mock<IDimensionTematicaRepository>();
        var organismos  = new Mock<IOrganismoResponsableRepository>();
        var origenes    = new Mock<IOrigenFinanciamientoRepository>();

        session.Setup(s => s.EstaAutenticado).Returns(autenticado);
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, "user.test", rol, null));
        session.Setup(s => s.PermisosActuales).Returns(permisos ?? new HashSet<string>());

        var svc = new ProyectoService(
            repo.Object, session.Object, new AuthorizationService(), audit.Object,
            zonas.Object, dimensiones.Object, organismos.Object, origenes.Object);
        return new Ctx(svc, repo, session, audit, zonas, dimensiones, organismos, origenes);
    }

    private static DatosProyecto Datos(
        string titulo = "Plaza", decimal? presupuesto = null, int? zonaId = null,
        PrioridadTarea prioridad = PrioridadTarea.Media) =>
        new(titulo, null, prioridad, null, presupuesto, zonaId, null, null, null);

    private static Proyecto Existente(EstadoProyecto estado = EstadoProyecto.Pendiente, decimal? presupuesto = null) =>
        new() { Id = 5, Titulo = "Plaza", Estado = estado, Presupuesto = presupuesto };

    private static HashSet<string> Permisos_(params string[] p) => new(p);

    // ── CrearAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearAsync_TituloVacio_Lanza()
    {
        var ctx = Crear();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.CrearAsync(Datos(titulo: "  ")));
        Assert.StartsWith("El título del proyecto es obligatorio.", ex.Message);
    }

    [Fact]
    public async Task CrearAsync_PresupuestoNegativo_Lanza()
    {
        var ctx = Crear();
        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.CrearAsync(Datos(presupuesto: -1m)));
        Assert.Equal("El presupuesto no puede ser negativo.", ex.Message);
        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Proyecto>()), Times.Never);
    }

    [Fact]
    public async Task CrearAsync_CatalogoInactivo_Lanza()
    {
        var ctx = Crear();
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = false });

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.CrearAsync(Datos(zonaId: 3)));

        Assert.Equal("La zona 'Centro' está inactiva.", ex.Message);
        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Proyecto>()), Times.Never);
    }

    [Fact]
    public async Task CrearAsync_CatalogoInexistente_Lanza()
    {
        var ctx = Crear();
        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.CrearAsync(Datos(zonaId: 9)));
        Assert.Equal("La zona 9 no existe.", ex.Message);
    }

    [Fact]
    public async Task CrearAsync_AuditaAltaProyecto_YFuerzaPendiente()
    {
        var ctx = Crear(idSesion: 7);
        Proyecto? guardado = null;
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Proyecto>()))
            .Callback<Proyecto>(p => guardado = p).ReturnsAsync(42);

        var id = await ctx.Svc.CrearAsync(Datos(presupuesto: 100m, prioridad: PrioridadTarea.Alta));

        Assert.Equal(42, id);
        Assert.NotNull(guardado);
        Assert.Equal(EstadoProyecto.Pendiente, guardado!.Estado);
        Assert.Equal(PrioridadTarea.Alta, guardado.Prioridad);
        Assert.Equal(7, guardado.CreadoPorUsuarioId);
        Assert.Null(guardado.FechaFin);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            7, AccionAuditada.AltaProyecto, "Proyecto", 42, It.Is<string>(d => d.Contains("Plaza"))), Times.Once);
    }

    // ── ModificarAsync ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(EstadoProyecto.Terminado)]
    [InlineData(EstadoProyecto.Cancelado)]
    public async Task ModificarAsync_ProyectoCerrado_Lanza(EstadoProyecto estado)
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(Existente(estado));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.ModificarAsync(5, Datos("Otro")));

        Assert.Equal("Reabrí el proyecto para editarlo.", ex.Message);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Proyecto>()), Times.Never);
    }

    [Fact]
    public async Task ModificarAsync_AuditaDiffDeCampos()
    {
        var ctx = Crear(idSesion: 3);
        var proyecto = Existente(presupuesto: 100m);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(proyecto);

        await ctx.Svc.ModificarAsync(5, Datos("Plaza Central", presupuesto: 250m));

        Assert.Equal("Plaza Central", proyecto.Titulo);
        Assert.Equal(250m, proyecto.Presupuesto);
        ctx.Repo.Verify(r => r.ActualizarAsync(proyecto), Times.Once);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            3, AccionAuditada.ModificacionProyecto, "Proyecto", 5,
            "Título: Plaza → Plaza Central; Presupuesto: 100 → 250"), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoAudita()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(Existente());

        await ctx.Svc.ModificarAsync(5, Datos());

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Proyecto>()), Times.Never);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ModificarAsync_CatalogoNuevo_DiffUsaNombres()
    {
        var ctx = Crear();
        var proyecto = Existente();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(proyecto);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = true });

        await ctx.Svc.ModificarAsync(5, Datos(zonaId: 3));

        Assert.Equal(3, proyecto.ZonaId);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionProyecto, "Proyecto", 5,
            "Zona: (sin asignar) → Centro"), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_CatalogoInactivoSinCambio_Guarda()
    {
        var ctx = Crear();
        var proyecto = Existente();
        proyecto.ZonaId = 3;
        proyecto.Zona = new Zona { Id = 3, Nombre = "Centro", Activo = false };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(proyecto);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = false });

        await ctx.Svc.ModificarAsync(5, Datos("Plaza Central", zonaId: 3));

        Assert.Equal("Plaza Central", proyecto.Titulo);
        Assert.Equal(3, proyecto.ZonaId);
        ctx.Repo.Verify(r => r.ActualizarAsync(proyecto), Times.Once);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionProyecto, "Proyecto", 5,
            "Título: Plaza → Plaza Central"), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_CambiaACatalogoInactivo_Lanza()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(Existente());
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = false });

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.ModificarAsync(5, Datos(zonaId: 3)));

        Assert.Equal("La zona 'Centro' está inactiva.", ex.Message);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Proyecto>()), Times.Never);
    }

    // ── CambiarEstadoAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CambiarEstadoAsync_AuditaEstadoAnteriorYNuevo()
    {
        var ctx = Crear(idSesion: 2);
        var proyecto = Existente();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(proyecto);

        await ctx.Svc.CambiarEstadoAsync(5, EstadoProyecto.EnCurso);

        Assert.Equal(EstadoProyecto.EnCurso, proyecto.Estado);
        ctx.Repo.Verify(r => r.ActualizarAsync(proyecto), Times.Once);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            2, AccionAuditada.CambioEstadoProyecto, "Proyecto", 5, "Pendiente → EnCurso"), Times.Once);
    }

    [Fact]
    public async Task CambiarEstadoAsync_TransicionInvalida_LanzaYNoAudita()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(Existente());

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CambiarEstadoAsync(5, EstadoProyecto.Terminado));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Proyecto>()), Times.Never);
    }

    [Fact]
    public async Task CambiarEstadoAsync_ProyectoInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ctx.Svc.CambiarEstadoAsync(99, EstadoProyecto.EnCurso));
    }

    // ── AgregarNotaAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarNotaAsync_TextoVacio_Lanza()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.AgregarNotaAsync(5, "   "));
    }

    [Fact]
    public async Task AgregarNotaAsync_EnProyectoCerrado_Permitido()
    {
        var ctx = Crear(idSesion: 4);
        var proyecto = Existente(EstadoProyecto.Terminado);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(proyecto);

        await ctx.Svc.AgregarNotaAsync(5, "  cierre  ");

        var nota = Assert.Single(proyecto.Notas);
        Assert.Equal("cierre", nota.Texto);
        Assert.Equal(4, nota.UsuarioId);
        Assert.False(nota.EsAutomatica);
        ctx.Repo.Verify(r => r.ActualizarAsync(proyecto), Times.Once);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            4, AccionAuditada.AltaNotaProyecto, "Proyecto", 5, "Nota: cierre"), Times.Once);
    }

    // ── ObtenerFichaAsync ─────────────────────────────────────────────────────

    private static Gasto G(int id, decimal monto) =>
        new() { Id = id, MontoTotal = monto, Detalle = $"g{id}", Fecha = new DateTime(2026, 10, id, 0, 0, 0, DateTimeKind.Utc),
                Proveedor = new Proveedor { Nombre = "Ferretería" } };

    private static void ConGastos(Ctx ctx, Proyecto proyecto, params Gasto[] gastos)
    {
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(proyecto.Id)).ReturnsAsync(proyecto);
        ctx.Repo.Setup(r => r.ListarGastosActivosAsync(proyecto.Id)).ReturnsAsync(gastos);
    }

    [Fact]
    public async Task ObtenerFichaAsync_SumaSoloGastosActivos()
    {
        var ctx = Crear();
        ConGastos(ctx, Existente(presupuesto: 500m), G(1, 100m), G(2, 50m));

        var ficha = await ctx.Svc.ObtenerFichaAsync(5);

        Assert.Equal(150m, ficha.TotalGastado);
        Assert.Equal(350m, ficha.Disponible);
        ctx.Repo.Verify(r => r.ListarGastosActivosAsync(5), Times.Once);
    }

    [Fact]
    public async Task ObtenerFichaAsync_SinPresupuesto_DisponibleNull()
    {
        var ctx = Crear();
        ConGastos(ctx, Existente(), G(1, 100m));

        var ficha = await ctx.Svc.ObtenerFichaAsync(5);

        Assert.Null(ficha.Disponible);
        Assert.Equal(100m, ficha.TotalGastado);
    }

    [Fact]
    public async Task ObtenerFichaAsync_PresupuestoCero_DisponibleNegativo()
    {
        var ctx = Crear();
        ConGastos(ctx, Existente(presupuesto: 0m), G(1, 100m), G(2, 50m));

        var ficha = await ctx.Svc.ObtenerFichaAsync(5);

        Assert.Equal(-150m, ficha.Disponible);
    }

    [Fact]
    public async Task ObtenerFichaAsync_OperadorSinVerFinanzas_GastosNull()
    {
        var ctx = Crear(RolUsuario.Operador, permisos: Permisos_(Permisos.VerProyectos));
        ConGastos(ctx, Existente(), G(1, 100m));

        var ficha = await ctx.Svc.ObtenerFichaAsync(5);

        Assert.Null(ficha.Gastos);
        Assert.Equal(100m, ficha.TotalGastado);
    }

    [Fact]
    public async Task ObtenerFichaAsync_OperadorConVerFinanzas_ListaGastos()
    {
        var ctx = Crear(RolUsuario.Operador, permisos: Permisos_(Permisos.VerProyectos, Permisos.VerFinanzas));
        ConGastos(ctx, Existente(), G(1, 100m), G(2, 50m));

        var ficha = await ctx.Svc.ObtenerFichaAsync(5);

        Assert.NotNull(ficha.Gastos);
        Assert.Equal(2, ficha.Gastos!.Count);
        Assert.Equal(new GastoDeProyectoDto(1, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "Ferretería", "g1", 100m),
            ficha.Gastos[0]);
    }

    [Fact]
    public async Task ObtenerFichaAsync_Admin_ListaGastosSinPermisosEnSesion()
    {
        var ctx = Crear(RolUsuario.Admin);
        ConGastos(ctx, Existente(), G(1, 100m));

        var ficha = await ctx.Svc.ObtenerFichaAsync(5);

        Assert.NotNull(ficha.Gastos);
    }

    // ── ListarOpcionesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListarOpcionesAsync_SoloPendientesYEnCurso_PorTitulo()
    {
        var ctx = Crear(RolUsuario.Operador);   // sin permisos de proyectos: igual puede listar opciones
        ctx.Repo.Setup(r => r.ListarAsync()).ReturnsAsync(new List<Proyecto>
        {
            new() { Id = 1, Titulo = "Zeta", Estado = EstadoProyecto.Pendiente },
            new() { Id = 2, Titulo = "Alfa", Estado = EstadoProyecto.EnCurso },
            new() { Id = 3, Titulo = "Beta", Estado = EstadoProyecto.Terminado },
            new() { Id = 4, Titulo = "Gama", Estado = EstadoProyecto.Cancelado },
        });

        var opciones = await ctx.Svc.ListarOpcionesAsync();

        Assert.Equal(
            new[] { new OpcionProyectoDto(2, "Alfa", EstadoProyecto.EnCurso), new OpcionProyectoDto(1, "Zeta", EstadoProyecto.Pendiente) },
            opciones);
    }

    // ── Matriz de permisos ────────────────────────────────────────────────────

    /// <summary>Operador con permisos mixtos: ve proyectos y finanzas pero NO gestiona; o al revés.</summary>
    public static IEnumerable<object[]> LlamadasSinPermiso()
    {
        // (nombre, permisos que SÍ tiene — todos menos el requerido, llamada)
        var gestion = new[] { Permisos.VerProyectos, Permisos.VerFinanzas, Permisos.GestionarTareas };
        var ver     = new[] { Permisos.GestionarProyectos, Permisos.VerFinanzas, Permisos.GestionarTareas };

        yield return Fila("Crear", gestion, s => s.CrearAsync(Datos()));
        yield return Fila("Modificar", gestion, s => s.ModificarAsync(5, Datos()));
        yield return Fila("CambiarEstado", gestion, s => s.CambiarEstadoAsync(5, EstadoProyecto.EnCurso));
        yield return Fila("AgregarNota", gestion, s => s.AgregarNotaAsync(5, "x"));
        yield return Fila("Listar", ver, s => s.ListarAsync());
        yield return Fila("ObtenerFicha", ver, s => s.ObtenerFichaAsync(5));
    }

    private static object[] Fila(string nombre, string[] permisos, Func<IProyectoService, Task> llamada) =>
        new object[] { nombre, permisos, llamada };

    [Theory]
    [MemberData(nameof(LlamadasSinPermiso))]
    public async Task Metodo_OperadorSinSuPermiso_LanzaUnauthorized(
        string nombre, string[] permisos, Func<IProyectoService, Task> llamada)
    {
        _ = nombre;
        var ctx = Crear(RolUsuario.Operador, permisos: new HashSet<string>(permisos));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => llamada(ctx.Svc));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Proyecto>()), Times.Never);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Proyecto>()), Times.Never);
        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
        ctx.Repo.Verify(r => r.ListarAsync(), Times.Never);
    }

    [Fact]
    public async Task ListarOpcionesAsync_SinSesion_LanzaUnauthorized()
    {
        var ctx = Crear(autenticado: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ListarOpcionesAsync());
        ctx.Repo.Verify(r => r.ListarAsync(), Times.Never);
    }

    // ── Contrato ──────────────────────────────────────────────────────────────

    [Fact]
    public void IProyectoService_ExponeExactamenteLosMetodosEsperados()
    {
        var esperados = new HashSet<string>
        {
            nameof(IProyectoService.CrearAsync),
            nameof(IProyectoService.ModificarAsync),
            nameof(IProyectoService.CambiarEstadoAsync),
            nameof(IProyectoService.AgregarNotaAsync),
            nameof(IProyectoService.ListarAsync),
            nameof(IProyectoService.ObtenerFichaAsync),
            nameof(IProyectoService.ListarOpcionesAsync),
        };

        var metodos = typeof(IProyectoService).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Equal(esperados, metodos);
    }
}
