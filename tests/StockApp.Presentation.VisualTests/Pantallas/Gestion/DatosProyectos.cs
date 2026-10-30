using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Proyectos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Proyectos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/gestion/proyecto*.html y proyectos*.html. Los proyectos vienen como los devuelve el
/// servidor: creación descendente. La ficha con sobregiro es la del plan vial (presupuesto $ 3.500.000, gastado
/// $ 3.612.400); la del Operador sin finanzas.ver es la del alumbrado (presupuesto $ 1.200.000, gastado $ 450.000).
/// </summary>
public static class DatosProyectos
{
    public static readonly UsuarioSesion UsuarioAdmin = DatosTareas.UsuarioAdmin;
    public static readonly UsuarioSesion UsuarioOperador = DatosTareas.UsuarioOperador;

    /// <summary>Operador sin finanzas.ver: gestiona proyectos y tareas, pero la API no le manda la lista de gastos.</summary>
    public static readonly string[] PermisosOperador = [Permisos.VerProyectos, Permisos.GestionarProyectos, Permisos.GestionarTareas];

    private static readonly Usuario Maria = new() { Id = 1, NombreUsuario = "mperez" };

    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Zona Centro = new() { Id = 1, Nombre = "Centro", Activo = true };
    private static readonly Zona Rambla = new() { Id = 2, Nombre = "Rambla", Activo = true };
    private static readonly DimensionTematica Infraestructura = new() { Id = 1, Nombre = "Infraestructura", Activo = true };
    private static readonly OrganismoResponsable Obras = new() { Id = 1, Nombre = "Dirección de Obras", Activo = true };
    private static readonly OrigenFinanciamiento Fondos = new() { Id = 1, Nombre = "Fondos departamentales", Activo = true };

    private static Proyecto P(int id, string titulo, EstadoProyecto estado, PrioridadTarea prioridad, decimal? presupuesto,
        DateTime? limite, Zona? zona, string? descripcion = null) => new()
    {
        Id = id, Titulo = titulo, Descripcion = descripcion, Estado = estado, Prioridad = prioridad, Presupuesto = presupuesto,
        FechaLimite = limite, FechaCreacion = DatosTareas.Hora(10, id, 8, 0), CreadoPorUsuarioId = 1,
        Zona = zona, ZonaId = zona?.Id,
    };

    public static readonly IReadOnlyList<Proyecto> Proyectos =
    [
        P(5, "Alumbrado de la rambla", EstadoProyecto.Pendiente, PrioridadTarea.Alta, 1200000m, Dia(2030, 3, 15), Rambla,
            "Recambio de las luminarias de la rambla por LED."),
        P(4, "Plan de mantenimiento vial del centro 2026", EstadoProyecto.EnCurso, PrioridadTarea.Media, 3500000m, Dia(2030, 12, 1), Centro,
            "Bacheo y señalización de las calles del centro."),
        P(3, "Arbolado urbano", EstadoProyecto.EnCurso, PrioridadTarea.Baja, null, null, null),
        P(2, "Plaza de deportes del Barrio Norte", EstadoProyecto.Terminado, PrioridadTarea.Media, 900000m, Dia(2026, 6, 30), null),
        P(1, "Bancos en la peatonal", EstadoProyecto.Cancelado, PrioridadTarea.Baja, null, null, Centro),
    ];

    static DatosProyectos()
    {
        var vial = Proyectos[1];
        vial.DimensionTematica = Infraestructura; vial.DimensionTematicaId = 1;
        vial.OrganismoResponsable = Obras; vial.OrganismoResponsableId = 1;
        vial.OrigenFinanciamiento = Fondos; vial.OrigenFinanciamientoId = 1;
        vial.Notas.Add(new NotaProyecto { Id = 1, ProyectoId = 4, Usuario = Maria, Fecha = DatosTareas.Hora(10, 20, 10, 30),
            Texto = "Se sumó la señalización horizontal de la calle 18 de Julio." });
    }

    public static readonly IReadOnlyList<GastoDeProyectoDto> GastosVial =
    [
        new(31, Dia(2026, 10, 2), "Barraca Sur", "Asfalto en frío para bacheo", 2150000m),
        new(32, Dia(2026, 10, 9), "Pinturas del Este", "Pintura vial blanca y amarilla", 862400m),
        new(33, Dia(2026, 10, 16), "Señales Uruguay", "Carteles de PARE y de sentido único", 600000m),
    ];

    private static readonly IReadOnlyList<Tarea> TareasVial =
    [
        new() { Id = 5, Titulo = "Reparar el bache de la calle Zorrilla", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
                FechaLimite = Dia(2030, 9, 30), ProyectoId = 4 },
        new() { Id = 2, Titulo = "Pintura de los cordones de la plaza", Estado = EstadoTarea.Terminada, Prioridad = PrioridadTarea.Baja,
                FechaLimite = Dia(2030, 1, 10), TomadaPor = Maria, TomadaPorUsuarioId = 1, ProyectoId = 4 },
    ];

    private static ICurrentSession Sesion(bool operador)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(operador ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(operador ? new HashSet<string>(PermisosOperador) : new HashSet<string>());
        sesion.Setup(s => s.UsuarioActual).Returns(operador ? UsuarioOperador : UsuarioAdmin);
        return sesion.Object;
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm, bool operador = false) =>
        operador
            ? DatosShell.Crear(UsuarioOperador, PermisosOperador, "Proyectos", vm)
            : DatosShell.Crear(UsuarioAdmin, [], "Proyectos", vm);

    /// <summary>Admin, filtro "Abiertos" (tres filas). La selección la pone la comparación.</summary>
    public static ShellMainViewModel Listado()
    {
        var servicio = new Mock<IProyectoService>();
        servicio.Setup(s => s.ListarAsync()).ReturnsAsync(Proyectos);
        return EnShell(new ProyectoListViewModel(servicio.Object, Sesion(false), Mock.Of<INavigationService>()));
    }

    private static ProyectoFormViewModel Ficha(FichaProyectoDto ficha, bool operador)
    {
        var servicio = new Mock<IProyectoService>();
        servicio.Setup(s => s.ObtenerFichaAsync(ficha.Proyecto.Id)).ReturnsAsync(ficha);
        var tareas = new Mock<ITareaService>();
        tareas.Setup(t => t.ListarPorProyectoAsync(4)).ReturnsAsync(TareasVial);
        tareas.Setup(t => t.ListarPorProyectoAsync(5)).ReturnsAsync([]);
        var zonas = new Mock<IZonaService>();
        zonas.Setup(z => z.ListarActivasAsync()).ReturnsAsync([Centro, Rambla]);
        var dimensiones = new Mock<IDimensionTematicaService>();
        dimensiones.Setup(d => d.ListarActivasAsync()).ReturnsAsync([Infraestructura]);
        var organismos = new Mock<IOrganismoResponsableService>();
        organismos.Setup(o => o.ListarActivasAsync()).ReturnsAsync([Obras]);
        var origenes = new Mock<IOrigenFinanciamientoService>();
        origenes.Setup(o => o.ListarActivasAsync()).ReturnsAsync([Fondos]);

        var vm = new ProyectoFormViewModel(servicio.Object, tareas.Object, zonas.Object, dimensiones.Object, organismos.Object,
            origenes.Object, Sesion(operador), Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
        vm.CargarParaVer(ficha.Proyecto.Id);
        return vm;
    }

    /// <summary>Admin: el plan vial En curso, gastado por encima del presupuesto ($ 112.400 de sobregiro) con sus tres gastos.</summary>
    public static ShellMainViewModel DetalleConSobregiro() =>
        EnShell(Ficha(new FichaProyectoDto(Proyectos[1], 3612400m, -112400m, GastosVial), operador: false));

    /// <summary>Operador sin finanzas.ver: el alumbrado Pendiente con presupuesto y disponible, sin la lista de gastos.</summary>
    public static ShellMainViewModel DetalleSinFinanzas() =>
        EnShell(Ficha(new FichaProyectoDto(Proyectos[0], 450000m, 750000m, null), operador: true), operador: true);
}
