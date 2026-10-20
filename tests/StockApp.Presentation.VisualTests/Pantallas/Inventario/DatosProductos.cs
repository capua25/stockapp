using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/*.html (los textos formateados están fijados en
/// DatosProductosTests). Un depósito municipal realista: un stock negativo, un decimal, un producto sin
/// categoría y uno inactivo. Doce filas: a 1280x760 entran sin scroll vertical.
/// </summary>
public static class DatosProductos
{
    public static readonly UsuarioSesion UsuarioAdmin = new(1, "mperez", RolUsuario.Admin, "María Pérez");

    private static readonly DateTime Alta = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

    private static ProductoDto P(int id, string codigo, string nombre, decimal costo, decimal stock,
        int unidadId, string unidad, int? categoriaId, string? categoria, bool activo = true) =>
        new(id, codigo, null, nombre, null, categoriaId, categoria, null, unidadId, unidad, costo, stock, 0m, activo, Alta);

    public static readonly IReadOnlyList<ProductoDto> Productos =
    [
        P(1, "MAT-0142", "Cemento portland 50 kg", 480m, 320m, 2, "Bolsa", 1, "Construcción"),
        P(2, "MAT-0198", "Arena gruesa", 890m, 45m, 3, "Metro cúbico", 1, "Construcción"),
        P(3, "PLO-0231", "Caño PVC 110 mm", 650m, 12m, 1, "Unidad", 6, "Plomería"),
        P(4, "PIN-0305", "Pintura látex blanca 20 L", 2340m, 6m, 4, "Balde", 5, "Pintura"),
        P(5, "SEG-0356", "Guantes de trabajo", 95m, 180m, 5, "Par", 7, "Seguridad"),
        P(6, "ELE-0402", "Lámpara LED 30 W", 410m, 8m, 1, "Unidad", 2, "Electricidad"),
        P(7, "ELE-0418", "Cable 2x1,5 mm", 38m, 1250m, 6, "Metro", 2, "Electricidad"),
        P(8, "LIM-0455", "Hipoclorito de sodio 5 L", 210m, -3m, 7, "Bidón", 4, "Limpieza"),
        P(9, "LIM-0470", "Bolsas de residuo 80x110", 175m, 60m, 8, "Paquete", 4, "Limpieza"),
        P(10, "HER-0512", "Pala de punta", 1150m, 14m, 1, "Unidad", null, null),
        P(11, "VIA-0533", "Pintura vial amarilla 4 L", 1890.5m, 22.5m, 9, "Lata", 5, "Pintura"),
        P(12, "HER-0560", "Carretilla reforzada", 3200m, 0m, 1, "Unidad", 3, "Herramientas", activo: false),
    ];

    public static readonly IReadOnlyList<UnidadMedida> Unidades =
    [
        new() { Id = 1, Nombre = "Unidad", Abreviatura = "u" },
        new() { Id = 2, Nombre = "Bolsa", Abreviatura = "bol" },
        new() { Id = 6, Nombre = "Metro", Abreviatura = "m" },
        new() { Id = 10, Nombre = "Litro", Abreviatura = "l" },
    ];

    public static readonly IReadOnlyList<Categoria> Categorias =
    [
        new() { Id = 1, Nombre = "Construcción" },
        new() { Id = 2, Nombre = "Electricidad" },
        new() { Id = 3, Nombre = "Herramientas" },
        new() { Id = 4, Nombre = "Limpieza" },
        new() { Id = 5, Nombre = "Pintura" },
        new() { Id = 6, Nombre = "Plomería" },
        new() { Id = 7, Nombre = "Seguridad" },
    ];

    /// <summary>El producto que abre el formulario en modo edición (el primero, con código de barras y descripción).</summary>
    public static readonly ProductoDto ProductoEditado = Productos[0] with
    {
        CodigoBarras = "7730123456789",
        Descripcion = "Bolsa de 50 kg para obras de mantenimiento urbano.",
    };

    public static ProductoListViewModel Listado(IReadOnlyList<ProductoDto> productos)
    {
        var servicio = new Mock<IProductoService>();
        servicio.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(productos);
        servicio.Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>())).ReturnsAsync(productos);
        // Debounce en cero: con los 300 ms reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado depende de cuánto tarde la máquina.
        return new ProductoListViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(), Mock.Of<StockApp.Application.Interfaces.ICurrentSession>(s => s.RolActual == StockApp.Domain.Enums.RolUsuario.Admin))
        { EsperaDebounce = TimeSpan.Zero };
    }

    /// <summary>Unidad más larga que el tope de las columnas medidas (240): se corta con elipsis.</summary>
    public const string UnidadLarga = "Metro cúbico puesto en obra con flete incluido";

    /// <summary>Los 12 productos con la unidad de "Arena gruesa" (fila 2) más larga que el tope.</summary>
    public static readonly IReadOnlyList<ProductoDto> ProductosConUnidadLarga =
        Productos.Select(p => p.Nombre == "Arena gruesa" ? p with { UnidadMedidaNombre = UnidadLarga } : p).ToList();

    /// <summary>La carga queda esperando al servidor: el listado está en "Cargando productos…".</summary>
    public static ProductoListViewModel ListadoQueNoTermina()
    {
        var servicio = new Mock<IProductoService>();
        // Nunca se completa, a propósito: el estado "cargando" queda congelado para la comparación con el mockup.
        servicio.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new TaskCompletionSource<IReadOnlyList<ProductoDto>>().Task);
        // Debounce en cero: con los 300 ms reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado depende de cuánto tarde la máquina.
        return new ProductoListViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(), Mock.Of<StockApp.Application.Interfaces.ICurrentSession>(s => s.RolActual == StockApp.Domain.Enums.RolUsuario.Admin))
        { EsperaDebounce = TimeSpan.Zero };
    }

    /// <summary>La carga responde 403 (UnauthorizedAccessException, como ApiErrores).</summary>
    public static ProductoListViewModel ListadoSinPermiso()
    {
        var servicio = new Mock<IProductoService>();
        servicio.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new UnauthorizedAccessException("403"));
        // Debounce en cero: con los 300 ms reales, un cambio de filtro dispara una carga diferida que compite por el turno con la carga explícita y el resultado depende de cuánto tarde la máquina.
        return new ProductoListViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(), Mock.Of<StockApp.Application.Interfaces.ICurrentSession>(s => s.RolActual == StockApp.Domain.Enums.RolUsuario.Admin))
        { EsperaDebounce = TimeSpan.Zero };
    }

    /// <summary>sinPermiso: la carga de unidades responde 403 (UnauthorizedAccessException), como ApiErrores.</summary>
    public static ProductoFormViewModel Formulario(bool sinPermiso = false)
    {
        var unidades = new Mock<IUnidadMedidaService>();
        if (sinPermiso)
        {
            unidades.Setup(u => u.GarantizarUnidadPorDefectoAsync()).ThrowsAsync(new UnauthorizedAccessException("403"));
        }
        else
        {
            unidades.Setup(u => u.GarantizarUnidadPorDefectoAsync()).ReturnsAsync(Unidades[0]);
        }

        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(Unidades);
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(Categorias);
        return new ProductoFormViewModel(Mock.Of<IProductoService>(), unidades.Object, categorias.Object, Mock.Of<INavigationService>());
    }

    private static ShellMainViewModel EnShell(ViewModelBase contenido) =>
        DatosShell.Crear(UsuarioAdmin, [], "Productos", contenido);

    public static ShellMainViewModel ConProductos() => EnShell(Listado(Productos));

    /// <summary>Los mismos 12 con el filtro de Estado en "Inactivos": queda solo la fila inactiva (productos-inactivos.html).</summary>
    public static ShellMainViewModel ConProductosInactivos()
    {
        var vm = Listado(Productos);
        vm.FiltroEstado = vm.FiltrosEstado.Single(o => o.Nombre == "Inactivos");
        return EnShell(vm);
    }

    public static ShellMainViewModel SinProductos() => EnShell(Listado([]));

    public static ShellMainViewModel Nuevo() => EnShell(Formulario());

    public static ShellMainViewModel Editar()
    {
        var vm = Formulario();
        vm.CargarParaEditar(ProductoEditado);
        return EnShell(vm);
    }

    /// <summary>Alta que el servidor rechazó por código repetido: el texto es el de ProductoService.AltaAsync.</summary>
    public static ShellMainViewModel ConError()
    {
        var vm = Formulario();
        vm.Codigo = "MAT-0142";
        vm.Nombre = "Cemento portland 50 kg";
        vm.MensajeError = "Ya existe un producto con el código 'MAT-0142'.";
        return EnShell(vm);
    }

    public static ShellMainViewModel SinPermiso() => EnShell(Formulario(sinPermiso: true));

    public static ShellMainViewModel Cargando() => EnShell(ListadoQueNoTermina());

    public static ShellMainViewModel SinPermisoListado() => EnShell(ListadoSinPermiso());

    public static ShellMainViewModel ConCeldaCortada() => EnShell(Listado(ProductosConUnidadLarga));
}
