using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Auth;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Datos fijos de docs/ui-v2/mockups/administracion/catalogos*.html y de los formularios de catálogo
/// (textos fijados en DatosAdministracionTests). Las fábricas devuelven el VM SIN cargar: la carga la dispara la vista.</summary>
public static class DatosCatalogos
{
    private static Categoria C(int id, string nombre, bool activo = true) => new() { Id = id, Nombre = nombre, Activo = activo };

    public static readonly IReadOnlyList<Categoria> Categorias =
    [
        C(1, "Electricidad"),
        C(2, "Plomería"),
        C(3, "Pinturería"),
        C(4, "Herramientas manuales"),
        C(5, "Materiales de construcción"),
        C(6, "Herramientas fuera de uso", activo: false),
    ];

    public static readonly IReadOnlyList<Proveedor> Proveedores =
    [
        new() { Id = 1, Nombre = "Barraca Carmelo", Telefono = "4542 2210", Email = "ventas@barraca.uy", Direccion = "18 de Julio 455", Notas = "Entrega martes y jueves" },
        new() { Id = 2, Nombre = "Ferretería El Tornillo", Telefono = "4542 0187", Email = "compras@eltornillo.com.uy",
                Direccion = "Ruta 21 km 262, esquina camino a Playa Fomento, Carmelo, Colonia", Notas = "Cuenta corriente a 30 días, pedir factura con orden de compra" },
        new() { Id = 3, Nombre = "Pinturas del Litoral", Telefono = "4552 3345", Email = "info@pinturaslitoral.uy", Direccion = "Av. Artigas 1280", Notas = null },
        new() { Id = 4, Nombre = "Eléctrica Rodríguez", Telefono = "099 123 456", Email = null, Direccion = null, Notas = null, Activo = false },
    ];

    public static readonly IReadOnlyList<LineaPoa> LineasPoa =
    [
        new() { Id = 1, Nombre = "Mantenimiento de caminería", Programa = "Obras viales", Ejercicio = 2026 },
        new() { Id = 2, Nombre = "Alumbrado público", Programa = "Servicios", Ejercicio = 2026 },
        new() { Id = 3, Nombre = "Espacios verdes", Programa = "Ambiente", Ejercicio = 2026 },
        new() { Id = 4, Nombre = "Plan de pluviales", Programa = "Obras viales", Ejercicio = 2026, Activo = false },
    ];

    public static readonly IReadOnlyList<Zona> Zonas =
    [
        new() { Id = 1, Nombre = "Centro" },
        new() { Id = 2, Nombre = "Rural norte" },
        new() { Id = 3, Nombre = "Playa Fomento" },
        new() { Id = 4, Nombre = "Barrio Sur" },
        new() { Id = 5, Nombre = "Costanera" },
    ];

    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "Rentas Generales" },
        new() { Id = 2, Nombre = "FIGM Literal B" },
    ];

    public static readonly UsuarioSesion UsuarioOperador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    /// <summary>Servicios de los diez catálogos, vacíos salvo los que cada fábrica configura.</summary>
    private sealed class Servicios
    {
        public Mock<ICategoriaService> Categorias { get; } = Nuevo<ICategoriaService, Categoria>(s => s.ListarTodasAsync());
        public Mock<IProveedorService> Proveedores { get; } = Nuevo<IProveedorService, Proveedor>(s => s.ListarTodosAsync());
        public Mock<IUnidadMedidaService> Unidades { get; } = Nuevo<IUnidadMedidaService, UnidadMedida>(s => s.ListarTodasAsync());
        public Mock<IFuenteFinanciamientoService> Fuentes { get; } = Nuevo<IFuenteFinanciamientoService, FuenteFinanciamiento>(s => s.ListarTodasAsync());
        public Mock<IRubroGastoService> Rubros { get; } = Nuevo<IRubroGastoService, RubroGasto>(s => s.ListarTodosAsync());
        public Mock<ILineaPoaService> Lineas { get; } = Nuevo<ILineaPoaService, LineaPoa>(s => s.ListarTodasAsync());
        public Mock<IZonaService> Zonas { get; } = Nuevo<IZonaService, Zona>(s => s.ListarTodasAsync());
        public Mock<IDimensionTematicaService> Dimensiones { get; } = Nuevo<IDimensionTematicaService, DimensionTematica>(s => s.ListarTodasAsync());
        public Mock<IOrganismoResponsableService> Organismos { get; } = Nuevo<IOrganismoResponsableService, OrganismoResponsable>(s => s.ListarTodasAsync());
        public Mock<IOrigenFinanciamientoService> Origenes { get; } = Nuevo<IOrigenFinanciamientoService, OrigenFinanciamiento>(s => s.ListarTodasAsync());

        private static Mock<TSvc> Nuevo<TSvc, TEntidad>(System.Linq.Expressions.Expression<Func<TSvc, Task<IReadOnlyList<TEntidad>>>> listar)
            where TSvc : class
        {
            var m = new Mock<TSvc>();
            m.Setup(listar).ReturnsAsync(new List<TEntidad>());
            return m;
        }
    }

    private static ICurrentSession Sesion(UsuarioSesion usuario, string[] permisos)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        return sesion.Object;
    }

    private static CatalogosViewModel Armar(Servicios svc, ClaveCatalogo abierto, UsuarioSesion? usuario = null, string[]? permisos = null)
    {
        var nav = Mock.Of<INavigationService>();
        var conf = Mock.Of<IConfirmacionService>();
        var vm = new CatalogosViewModel(
            Sesion(usuario ?? DatosProductos.UsuarioAdmin, permisos ?? []),
            new CategoriaListViewModel(svc.Categorias.Object, nav, conf),
            new ProveedorListViewModel(svc.Proveedores.Object, nav, conf),
            new UnidadMedidaListViewModel(svc.Unidades.Object, nav, conf),
            new FuenteFinanciamientoListViewModel(svc.Fuentes.Object, nav, conf),
            new RubroGastoListViewModel(svc.Rubros.Object, nav, conf),
            new LineaPoaListViewModel(svc.Lineas.Object, nav, conf),
            new ZonaListViewModel(svc.Zonas.Object, nav, conf),
            new DimensionTematicaListViewModel(svc.Dimensiones.Object, nav, conf),
            new OrganismoResponsableListViewModel(svc.Organismos.Object, nav, conf),
            new OrigenFinanciamientoListViewModel(svc.Origenes.Object, nav, conf));
        vm.Seleccionar(abierto);
        return vm;
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm, UsuarioSesion? usuario = null, string[]? permisos = null) =>
        DatosShell.Crear(usuario ?? DatosProductos.UsuarioAdmin, permisos ?? [], "Catalogos", vm);

    public static ShellMainViewModel ConCategorias()
    {
        var svc = new Servicios();
        svc.Categorias.Setup(s => s.ListarTodasAsync()).ReturnsAsync(Categorias);
        return EnShell(Armar(svc, ClaveCatalogo.Categorias));
    }

    public static ShellMainViewModel ConProveedores()
    {
        var svc = new Servicios();
        svc.Proveedores.Setup(s => s.ListarTodosAsync()).ReturnsAsync(Proveedores);
        return EnShell(Armar(svc, ClaveCatalogo.Proveedores));
    }

    public static ShellMainViewModel ConLineasPoa()
    {
        var svc = new Servicios();
        svc.Lineas.Setup(s => s.ListarTodasAsync()).ReturnsAsync(LineasPoa);
        return EnShell(Armar(svc, ClaveCatalogo.LineasPoa));
    }

    /// <summary>Operador con catalogo.maestras: ve INVENTARIO y TAREAS, no FINANZAS.</summary>
    public static ShellMainViewModel Operador()
    {
        var svc = new Servicios();
        svc.Zonas.Setup(s => s.ListarTodasAsync()).ReturnsAsync(Zonas);
        string[] permisos = [Permisos.GestionarTablasMaestras];
        return EnShell(Armar(svc, ClaveCatalogo.Zonas, UsuarioOperador, permisos), UsuarioOperador, permisos);
    }

    public static ShellMainViewModel Cargando()
    {
        var svc = new Servicios();
        svc.Categorias.Setup(s => s.ListarTodasAsync()).Returns(new TaskCompletionSource<IReadOnlyList<Categoria>>().Task);
        return EnShell(Armar(svc, ClaveCatalogo.Categorias));
    }

    public static ShellMainViewModel SinConexion()
    {
        var svc = new Servicios();
        svc.Categorias.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new ServidorNoDisponibleException());
        return EnShell(Armar(svc, ClaveCatalogo.Categorias));
    }

    /// <summary>403 al listar las fuentes: el master queda y el detalle dice "Sin permiso".</summary>
    public static ShellMainViewModel SinPermiso()
    {
        var svc = new Servicios();
        svc.Fuentes.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        return EnShell(Armar(svc, ClaveCatalogo.FuentesFinanciamiento));
    }

    public static ShellMainViewModel Vacio() => EnShell(Armar(new Servicios(), ClaveCatalogo.UnidadesMedida));

    public static ShellMainViewModel SinResultados()
    {
        var svc = new Servicios();
        svc.Categorias.Setup(s => s.ListarTodasAsync()).ReturnsAsync(Categorias);
        var vm = Armar(svc, ClaveCatalogo.Categorias);
        vm.Grupos[0].Entradas[0].Listado.Busqueda = "zzz";
        return EnShell(vm);
    }

    // ── formularios ───────────────────────────────────────────────────────────

    public static ShellMainViewModel CategoriaNueva() =>
        EnShell(new CategoriaFormViewModel(Mock.Of<ICategoriaService>(), Mock.Of<INavigationService>()));

    /// <summary>Alta con un nombre repetido: la regla del servidor sale en la Alerta de error arriba del pie.</summary>
    public static ShellMainViewModel CategoriaError()
    {
        var vm = new CategoriaFormViewModel(Mock.Of<ICategoriaService>(), Mock.Of<INavigationService>())
        {
            Nombre = "Electricidad",
            MensajeError = "Ya existe una categoría con ese nombre.",
        };
        return EnShell(vm);
    }

    public static ShellMainViewModel ProveedorEditar()
    {
        var vm = new ProveedorFormViewModel(Mock.Of<IProveedorService>(), Mock.Of<INavigationService>());
        vm.CargarParaEditar(Proveedores[0]);
        return EnShell(vm);
    }

    public static ShellMainViewModel RubroNuevo() =>
        EnShell(new RubroGastoFormViewModel(Mock.Of<IRubroGastoService>(), Mock.Of<INavigationService>()));

    /// <summary>Línea con dos asignaciones. Las filas se arman en InicializarAsync, que dispara la vista.</summary>
    public static ShellMainViewModel LineaPoaEditar()
    {
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes);
        var vm = new LineaPoaFormViewModel(Mock.Of<ILineaPoaService>(), fuentes.Object, Mock.Of<INavigationService>());
        vm.CargarParaEditar(new LineaPoa
        {
            Id = 1, Nombre = "Mantenimiento de caminería", Programa = "Obras viales", Ejercicio = 2026,
            Asignaciones =
            [
                new() { FuenteFinanciamientoId = 1, FuenteFinanciamiento = Fuentes[0], Monto = 1_250_000m },
                new() { FuenteFinanciamientoId = 2, FuenteFinanciamiento = Fuentes[1], Monto = 380_000m },
            ],
        });
        return EnShell(vm);
    }
}
