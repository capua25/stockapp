using Moq;
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/gestion/tarea*.html y tareas*.html (textos fijados en DatosGestionTests). Las
/// tareas vienen como las devuelve el servidor: creación descendente. Los vencimientos están lejos de "hoy" (la vencida
/// el 30/09/2026, el resto en 2030) porque TareaFila calcula con el reloj real.
/// </summary>
public static class DatosTareas
{
    public static readonly UsuarioSesion UsuarioAdmin = new(1, "mperez", RolUsuario.Admin, "María Pérez");
    public static readonly UsuarioSesion UsuarioOperador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    private static readonly Usuario Maria = new() { Id = 1, NombreUsuario = "mperez" };
    private static readonly Usuario Sosa = new() { Id = 3, NombreUsuario = "jsosa" };

    public static readonly Proyecto ProyectoVial = new() { Id = 1, Titulo = "Plan de mantenimiento vial del centro 2026" };
    public static readonly Proyecto ProyectoArbolado = new() { Id = 2, Titulo = "Arbolado urbano" };
    public static readonly Proyecto ProyectoAlumbrado = new() { Id = 3, Titulo = "Alumbrado de la rambla" };

    private static readonly DocumentoAdministrativo Expediente45 = new()
    {
        Id = 45, Numero = "45", Anio = 2026, Tipo = TipoDocumento.Expediente, Descripcion = "Bacheo de calles céntricas",
    };

    /// <summary>Hora LOCAL fija de 2026 convertida a UTC: la nota (UTC a local) muestra lo mismo en cualquier huso.</summary>
    public static DateTime Hora(int mes, int dia, int hora, int minuto) =>
        new DateTime(2026, mes, dia, hora, minuto, 0, DateTimeKind.Local).ToUniversalTime();

    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    public static readonly IReadOnlyList<NotaTarea> Notas =
    [
        new() { Id = 1, TareaId = 4, Usuario = Maria, Fecha = Hora(10, 12, 9, 15), Texto = "Tarea tomada por mperez.", EsAutomatica = true },
        new() { Id = 2, TareaId = 4, Usuario = Maria, Fecha = Hora(10, 13, 11, 40), Texto = "Se podaron los plátanos del lado norte; falta el lado sur." },
        new() { Id = 3, TareaId = 4, Usuario = Maria, Fecha = Hora(10, 15, 16, 5), Texto = "Quedan para el lunes los ejemplares junto a la escuela, porque hay que coordinar el corte de tránsito." },
    ];

    private static Tarea T(int id, string titulo, EstadoTarea estado, PrioridadTarea prioridad, DateTime? limite, Usuario? tomadaPor,
        Proyecto? proyecto, DocumentoAdministrativo? exp = null, string? descripcion = null) => new()
    {
        Id = id, Titulo = titulo, Descripcion = descripcion, Estado = estado, Prioridad = prioridad, FechaLimite = limite,
        CreadaPorUsuarioId = 1, CreadaPor = Maria, FechaCreacion = Hora(10, id, 8, 0),
        TomadaPor = tomadaPor, TomadaPorUsuarioId = tomadaPor?.Id,
        Proyecto = proyecto, ProyectoId = proyecto?.Id,
        DocumentoAdministrativo = exp, DocumentoAdministrativoId = exp?.Id,
    };

    public static readonly IReadOnlyList<Tarea> Tareas =
    [
        T(6, "Relevar las luminarias de la rambla", EstadoTarea.Pendiente, PrioridadTarea.Alta, Dia(2030, 3, 15), null,
            ProyectoAlumbrado),
        T(5, "Reparar el bache de la calle Zorrilla", EstadoTarea.Pendiente, PrioridadTarea.Media, Dia(2026, 9, 30), null,
            ProyectoVial, Expediente45),
        T(4, "Poda de los plátanos de la avenida Artigas", EstadoTarea.EnCurso, PrioridadTarea.Alta, Dia(2030, 2, 1), Maria,
            ProyectoArbolado,
            descripcion: "Podar los ejemplares secos y retirar los restos antes del fin de semana."),
        T(3, "Limpieza de las cunetas del Barrio Norte", EstadoTarea.EnCurso, PrioridadTarea.Media, Dia(2030, 5, 20), Sosa,
            null),
        T(2, "Pintura de los cordones de la plaza", EstadoTarea.Terminada, PrioridadTarea.Baja, Dia(2030, 1, 10), Maria,
            ProyectoVial),
        T(1, "Instalar bancos en la peatonal", EstadoTarea.Cancelada, PrioridadTarea.Media, Dia(2030, 4, 4), null,
            null),
    ];

    static DatosTareas() => Tareas[2].Notas.AddRange(Notas);

    private static ICurrentSession Sesion(bool operador)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(operador ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(operador
            ? new HashSet<string> { Permisos.GestionarTareas }
            : new HashSet<string>());
        sesion.Setup(s => s.UsuarioActual).Returns(operador ? UsuarioOperador : UsuarioAdmin);
        return sesion.Object;
    }

    public static TareaListViewModel ListadoVm(Func<Task<IReadOnlyList<Tarea>>> responder, bool operador = false)
    {
        var servicio = new Mock<ITareaService>();
        servicio.Setup(s => s.ListarAsync()).Returns(responder);
        return new TareaListViewModel(servicio.Object, Sesion(operador), Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    /// <summary>Panel de clasificación con los proyectos abiertos de los datos fijos ("(ninguno)" y sus opciones).</summary>
    public static ClasificacionTareaPanelViewModel Panel()
    {
        var proyectos = new Mock<IProyectoService>();
        proyectos.Setup(s => s.ListarOpcionesAsync()).ReturnsAsync(
        [
            new OpcionProyectoDto(ProyectoAlumbrado.Id, ProyectoAlumbrado.Titulo, EstadoProyecto.Pendiente),
            new OpcionProyectoDto(ProyectoArbolado.Id, ProyectoArbolado.Titulo, EstadoProyecto.EnCurso),
            new OpcionProyectoDto(ProyectoVial.Id, ProyectoVial.Titulo, EstadoProyecto.EnCurso),
        ]);
        var documentos = new Mock<IDocumentoAdministrativoService>();
        documentos.Setup(s => s.ObtenerPorIdAsync(45)).ReturnsAsync(Expediente45);
        return new ClasificacionTareaPanelViewModel(proyectos.Object, documentos.Object, Mock.Of<IConfirmacionService>());
    }

    public static TareaFormViewModel Formulario(bool operador = false)
    {
        var servicio = new Mock<ITareaService>();
        return new TareaFormViewModel(servicio.Object, Sesion(operador), Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(),
            Panel(), Mock.Of<IClasificacionTareaDialogService>());
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm, bool operador = false) =>
        operador
            ? DatosShell.Crear(UsuarioOperador, [Permisos.GestionarTareas], "Tareas", vm)
            : DatosShell.Crear(UsuarioAdmin, [], "Tareas", vm);

    private static Task<IReadOnlyList<Tarea>> Lista() => Task.FromResult(Tareas);

    /// <summary>Admin, filtro "Abiertas" (4 filas). La selección la pone la comparación.</summary>
    public static ShellMainViewModel Listado() => EnShell(ListadoVm(Lista));
    public static ShellMainViewModel ListadoOperador() => EnShell(ListadoVm(Lista, operador: true), operador: true);
    public static ShellMainViewModel Cargando() => EnShell(ListadoVm(() => new TaskCompletionSource<IReadOnlyList<Tarea>>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(ListadoVm(() => Task.FromException<IReadOnlyList<Tarea>>(new ServidorNoDisponibleException())));
    public static ShellMainViewModel SinPermiso() =>
        EnShell(ListadoVm(() => Task.FromException<IReadOnlyList<Tarea>>(new UnauthorizedAccessException())));
    public static ShellMainViewModel SinTareas() => EnShell(ListadoVm(() => Task.FromResult<IReadOnlyList<Tarea>>([])));

    /// <summary>Filtro "Canceladas" sin ninguna cancelada: hay tareas, ninguna en ese estado.</summary>
    public static ShellMainViewModel SinResultados()
    {
        var vm = ListadoVm(() => Task.FromResult<IReadOnlyList<Tarea>>(Tareas.Where(t => t.Estado != EstadoTarea.Cancelada).ToList()));
        vm.FiltroEstado = vm.FiltrosEstado.Single(o => o.Nombre == "Canceladas");
        return EnShell(vm);
    }

    public static ShellMainViewModel Nueva()
    {
        var vm = Formulario();
        vm.CargarParaCrear();
        return EnShell(vm);
    }

    /// <summary>Título tocado y vacío: el error debajo de su campo y Guardar deshabilitado.</summary>
    public static ShellMainViewModel NuevaConErrorDeCampo()
    {
        var vm = Formulario();
        vm.CargarParaCrear();
        vm.MarcarTocado(nameof(TareaFormViewModel.Titulo));
        return EnShell(vm);
    }

    /// <summary>La tarea 4 (En curso, tomada por mperez) con sus tres notas.</summary>
    public static ShellMainViewModel Detalle()
    {
        var vm = Formulario();
        vm.CargarParaVer(Tareas[2]);
        return EnShell(vm);
    }

    public static ShellMainViewModel DetalleOperador()
    {
        var vm = Formulario(operador: true);
        vm.CargarParaVer(Tareas[2]);
        return EnShell(vm, operador: true);
    }

    /// <summary>Diálogo de reclasificar con la clasificación de la tarea 5 precargada (incluye el expediente 45/2026).</summary>
    public static ReclasificarTareaDialogViewModel Reclasificar()
    {
        var panel = Panel();
        panel.InicializarAsync(new DatosClasificacionTarea(ProyectoVial.Id, Expediente45.Id)).GetAwaiter().GetResult();
        return new ReclasificarTareaDialogViewModel(panel);
    }
}
