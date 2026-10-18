using Moq;
using StockApp.ApiClient;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Reportes;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/reportes/*.html (los textos formateados los fija DatosReportesTests). Un depósito
/// municipal con fechas de setiembre y octubre de 2026: nada es relativo a "hoy". Cada fábrica devuelve el shell con el
/// reporte ya cargado (o en el estado que dibuja el mockup).
/// </summary>
public static class DatosReportes
{
    public static readonly UsuarioSesion UsuarioOperador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    /// <summary>Hora LOCAL fija convertida a UTC: la celda (UTC a local) muestra lo mismo en cualquier huso.</summary>
    public static DateTime Fecha(int mes, int dia, int hora, int minuto) => DatosHistorial.Fecha(mes, dia, hora, minuto);

    // ── Valorización (8 productos, uno con stock negativo) y Stock por categoría (5, una con stock total negativo) ──

    private static ValorizacionItemDto V(int id, string codigo, string nombre, string categoria, decimal stock, decimal costo) =>
        new(id, codigo, nombre, categoria, stock, costo, stock * costo);

    public static readonly IReadOnlyList<ValorizacionItemDto> ItemsValorizacion =
    [
        V(1, "MAT-0142", "Cemento portland 50 kg", "Construcción", 320m, 480m),
        V(2, "MAT-0198", "Arena gruesa", "Construcción", 45m, 890m),
        V(4, "PIN-0305", "Pintura látex blanca 20 L", "Pintura", 6m, 2340m),
        V(5, "SEG-0356", "Guantes de trabajo", "Seguridad", 180m, 95m),
        V(6, "ELE-0402", "Lámpara LED 30 W", "Electricidad", 8m, 410m),
        V(7, "ELE-0418", "Cable 2x1,5 mm", "Electricidad", 1250m, 38m),
        V(8, "LIM-0455", "Hipoclorito de sodio 5 L", "Limpieza", -3m, 210m),
        V(11, "VIA-0533", "Pintura vial amarilla 4 L", "Pintura", 22.5m, 1890.5m),
    ];

    public static readonly ValorizacionReporteDto ValorizacionDatos =
        new(ItemsValorizacion, new ValorizacionTotalesDto(ItemsValorizacion.Sum(i => i.ValorCosto)));

    public static readonly IReadOnlyList<StockCategoriaDto> CategoriasDatos =
    [
        new("Construcción", 2, 365m, 193_650m),
        new("Pintura", 2, 28.5m, 56_576.25m),
        new("Seguridad", 1, 180m, 17_100m),
        new("Electricidad", 2, 1258m, 50_780m),
        new("Limpieza", 1, -3m, -630m),
    ];

    // ── Historial de UN producto (9 movimientos, uno deja el stock en negativo), el más nuevo primero ─────────

    private const string Maria = "María Pérez";
    private const string Deposito = "Operador de Depósito Central";

    private static MovimientoHistorialDto M(int id, TipoMovimiento tipo, MotivoMovimiento motivo, decimal cantidad, decimal precio,
        decimal anterior, decimal nuevo, string? comentario, DateTime fecha, int usuarioId, string usuario) =>
        new(id, 1, "Cemento portland 50 kg", tipo, motivo, cantidad, precio, anterior, nuevo, comentario, fecha, usuarioId, usuario);

    public static readonly IReadOnlyList<MovimientoHistorialDto> MovimientosDelProducto =
    [
        M(209, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 40m, 480m, 25m, 65m, "Remito 0042-00001234", Fecha(10, 7, 14, 35), 1, Maria),
        M(208, TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, 45m, 0m, -20m, 25m, "Diferencia de conteo del inventario de setiembre.", Fecha(10, 3, 10, 15), 1, Maria),
        M(207, TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 45m, 0m, 25m, -20m, "Reposición de emergencia de la escuela 12", Fecha(9, 30, 16, 40), 2, Deposito),
        M(206, TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 160m, 0m, 185m, 25m, "Cordón cuneta del barrio sur", Fecha(9, 24, 9, 5), 2, Deposito),
        M(205, TipoMovimiento.Salida, MotivoMovimiento.Merma, 15m, 0m, 200m, 185m, "Bolsas rotas por humedad", Fecha(9, 19, 15, 20), 2, Deposito),
        M(204, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 100m, 475m, 100m, 200m, "Remito 0042-00001190", Fecha(9, 15, 13, 0), 1, Maria),
        M(203, TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 120m, 0m, 220m, 100m, "Bacheo de la calle 18 de Julio", Fecha(9, 9, 11, 10), 2, Deposito),
        M(202, TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 80m, 0m, 300m, 220m, "Obra de la plazoleta", Fecha(9, 4, 9, 30), 2, Deposito),
        M(201, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 300m, 470m, 0m, 300m, "Remito 0042-00001101", Fecha(9, 1, 8, 0), 1, Maria),
    ];

    // ── Productos más movidos (top 10) ─────────────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<MasMovidoDto> MasMovidosTop =
    [
        new(1, "MAT-0142", "Cemento portland 50 kg", 18, 1240m),
        new(7, "ELE-0418", "Cable 2x1,5 mm", 14, 8350.5m),
        new(5, "SEG-0356", "Guantes de trabajo", 11, 640m),
        new(8, "LIM-0455", "Hipoclorito de sodio 5 L", 9, 46m),
        new(6, "ELE-0402", "Lámpara LED 30 W", 8, 52m),
        new(2, "MAT-0198", "Arena gruesa", 7, 210m),
        new(9, "LIM-0470", "Bolsas de residuo 80x110", 6, 480m),
        new(4, "PIN-0305", "Pintura látex blanca 20 L", 5, 31m),
        new(10, "HER-0512", "Pala de punta", 4, 22m),
        new(11, "VIA-0533", "Pintura vial amarilla 4 L", 3, 67.5m),
    ];

    // ── Log de auditoría (11 registros: con 12 la última fila no entra en 1280x760 y la grilla mostraría una barra vertical) ───────────────────────────────────────────────────────────────

    public const string DetalleLargo =
        "Precio de costo de 'Cemento portland 50 kg' de 450,00 a 480,00 por la actualización de la lista del proveedor de setiembre.";

    private static AuditoriaItemDto A(DateTime fecha, string usuario, AccionAuditada accion, string entidad, int id, string detalle) =>
        new(fecha, usuario, accion, entidad, id, detalle);

    public static readonly IReadOnlyList<AuditoriaItemDto> Registros =
    [
        A(Fecha(10, 7, 14, 35), "mperez", AccionAuditada.RegistroMovimiento, "MovimientoStock", 209, "Entrada de 40 unidades"),
        A(Fecha(10, 7, 9, 12), "mperez", AccionAuditada.CambioPrecio, "Producto", 1, DetalleLargo),
        A(Fecha(10, 6, 16, 48), "jrodriguez", AccionAuditada.RegistroMovimiento, "MovimientoStock", 208, "Salida de 25,5 unidades"),
        A(Fecha(10, 3, 10, 15), "mperez", AccionAuditada.RecalculoStock, "Producto", 1, "Stock recalculado: 25"),
        A(Fecha(10, 1, 8, 30), "mperez", AccionAuditada.AltaProducto, "Producto", 12, "Carretilla reforzada"),
        A(Fecha(9, 30, 17, 5), "jrodriguez", AccionAuditada.RegistroMovimiento, "MovimientoStock", 207, "Salida de 45 unidades"),
        A(Fecha(9, 26, 11, 20), "mperez", AccionAuditada.ModificacionProducto, "Producto", 8, "Stock mínimo: 5 a 10"),
        A(Fecha(9, 22, 15, 0), "mperez", AccionAuditada.AltaUsuario, "Usuario", 3, "mlopez"),
        A(Fecha(9, 22, 15, 4), "mperez", AccionAuditada.CambioRol, "Usuario", 3, "Operador"),
        A(Fecha(9, 18, 9, 40), "mlopez", AccionAuditada.AltaCategoria, "Categoria", 6, "Herramientas"),
        A(Fecha(9, 12, 12, 10), "mperez", AccionAuditada.BajaProducto, "Producto", 13, "Pala ancha"),
    ];

    public static readonly IReadOnlyList<UsuarioDto> Usuarios =
    [
        new(1, "mperez", "María Pérez", RolUsuario.Admin, true, new DateTime(2026, 3, 2)),
        new(2, "jrodriguez", "Juan Rodríguez", RolUsuario.Operador, true, new DateTime(2026, 3, 9)),
        new(3, "mlopez", "Marta López", RolUsuario.Operador, true, new DateTime(2026, 9, 22)),
    ];

    // ── Estadística de tareas (5 zonas) ───────────────────────────────────────────────────────────────

    public static readonly IReadOnlyList<FilaReporteTareas> Zonas =
    [
        new("Zona Centro", 4, 6, 12, 1, 23),
        new("Zona Norte", 7, 3, 9, 2, 21),
        new("Zona Sur", 2, 5, 14, 0, 21),
        new("Zona Este", 5, 2, 6, 3, 16),
        new("Zona Oeste", 1, 4, 8, 1, 14),
    ];

    public static readonly ReporteTareasDto ReporteZonas = new(Zonas, Zonas.Sum(z => z.Total));

    public static readonly IReadOnlyList<FilaReporteTareas> ZonasPorCierre =
    [
        new("Zona Centro", 0, 0, 12, 1, 13),
        new("Zona Norte", 0, 0, 9, 2, 11),
        new("Zona Sur", 0, 0, 14, 0, 14),
        new("Zona Este", 0, 0, 6, 3, 9),
        new("Zona Oeste", 0, 0, 8, 1, 9),
    ];

    public static readonly ReporteTareasDto ReporteZonasPorCierre = new(ZonasPorCierre, ZonasPorCierre.Sum(z => z.Total));

    // ── Armado de los VMs ────────────────────────────────────────────────────────────────────────────

    private static Mock<ICurrentSession> Sesion(UsuarioSesion usuario, params string[] permisos)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        return sesion;
    }

    private static ShellMainViewModel EnShell(ViewModelBase contenido) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Reportes", contenido);

    private static readonly INavigationService Navegacion = Mock.Of<INavigationService>();

    public static ReportesViewModel Hub(UsuarioSesion usuario, params string[] permisos) =>
        new(Sesion(usuario, permisos).Object, Navegacion);

    /// <summary>Forma de respuesta de un servicio: datos, pendiente (Cargando), falla (403 o sin servidor).</summary>
    private static Func<Task<T>> Respuesta<T>(T? datos = default, bool pendiente = false, Exception? falla = null) =>
        pendiente ? () => new TaskCompletionSource<T>().Task
        : falla is not null ? () => Task.FromException<T>(falla)
        : () => Task.FromResult(datos!);

    private static ValorizacionViewModel ValorizacionVm(Func<Task<ValorizacionReporteDto>> responder)
    {
        var servicio = new Mock<IReporteStockService>();
        servicio.Setup(s => s.ObtenerValorizacionAsync()).Returns(responder);
        return new ValorizacionViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), Sesion(DatosProductos.UsuarioAdmin).Object, Navegacion);
    }

    private static ShellMainViewModel Cargado(ValorizacionViewModel vm, bool esperar = true)
    {
        var tarea = vm.CargarAsync();
        if (esperar)
            tarea.GetAwaiter().GetResult();
        return EnShell(vm);
    }

    public static ShellMainViewModel HubAdmin() => EnShell(Hub(DatosProductos.UsuarioAdmin));
    public static ShellMainViewModel HubOperador() =>
        DatosShell.Crear(UsuarioOperador, [Permisos.VerReportes], "Reportes", Hub(UsuarioOperador, Permisos.VerReportes));

    public static ShellMainViewModel Valorizacion() => Cargado(ValorizacionVm(Respuesta(ValorizacionDatos)));
    public static ShellMainViewModel ValorizacionCargando() => Cargado(ValorizacionVm(Respuesta<ValorizacionReporteDto>(pendiente: true)), esperar: false);
    public static ShellMainViewModel ValorizacionSinConexion() =>
        Cargado(ValorizacionVm(Respuesta<ValorizacionReporteDto>(falla: new ServidorNoDisponibleException())));
    public static ShellMainViewModel ValorizacionSinPermiso() =>
        Cargado(ValorizacionVm(Respuesta<ValorizacionReporteDto>(falla: new UnauthorizedAccessException("403"))));
    public static ShellMainViewModel ValorizacionVacia() =>
        Cargado(ValorizacionVm(Respuesta(new ValorizacionReporteDto([], new ValorizacionTotalesDto(0m)))));

    public static ShellMainViewModel StockCategoria()
    {
        var servicio = new Mock<IReporteStockService>();
        servicio.Setup(s => s.ObtenerStockPorCategoriaAsync()).ReturnsAsync(CategoriasDatos);
        var vm = new StockCategoriaViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), Sesion(DatosProductos.UsuarioAdmin).Object, Navegacion);
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    private static ProductoDto Cemento => new(1, "MAT-0142", null, "Cemento portland 50 kg", null, 1, "Construcción", null, 2, "Bolsa", 480m, 320m, 0m, true,
        new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static HistorialPorProductoViewModel HistorialVm()
    {
        var servicio = new Mock<IReporteStockService>();
        servicio.Setup(s => s.ObtenerHistorialPorProductoAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(MovimientosDelProducto);
        return new HistorialPorProductoViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<IProductoService>(), Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(),
            Sesion(DatosProductos.UsuarioAdmin).Object, Navegacion);
    }

    public static ShellMainViewModel HistorialProducto()
    {
        var vm = HistorialVm();
        vm.ProductoSeleccionado = Cemento;
        vm.FechaDesde = new DateTime(2026, 9, 1);
        vm.FechaHasta = new DateTime(2026, 10, 15);
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    public static ShellMainViewModel HistorialSinProducto()
    {
        var vm = HistorialVm();
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    private static MasMovidosViewModel MasMovidosVm()
    {
        var servicio = new Mock<IReporteStockService>();
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>())).ReturnsAsync(MasMovidosTop);
        var vm = new MasMovidosViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), Sesion(DatosProductos.UsuarioAdmin).Object, Navegacion);
        vm.FechaDesde = new DateTime(2026, 9, 1);
        vm.FechaHasta = new DateTime(2026, 10, 15);
        vm.TopN = 10;
        return vm;
    }

    public static ShellMainViewModel MasMovidos()
    {
        var vm = MasMovidosVm();
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    /// <summary>Desde posterior a Hasta tras una carga buena: el mensaje de error aparece sobre la grilla anterior.</summary>
    public static ShellMainViewModel MasMovidosErrorFechas()
    {
        var vm = MasMovidosVm();
        vm.CargarAsync().GetAwaiter().GetResult();
        vm.FechaDesde = new DateTime(2026, 9, 30);
        vm.FechaHasta = new DateTime(2026, 9, 1);
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    public static ShellMainViewModel Auditoria()
    {
        var servicio = new Mock<IAuditoriaQueryService>();
        servicio.Setup(s => s.ObtenerLogAsync(It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>())).ReturnsAsync(Registros);
        var usuarios = new Mock<IUsuarioService>();
        usuarios.Setup(u => u.ListarAsync()).ReturnsAsync(Usuarios);
        var vm = new AuditoriaLogViewModel(servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(),
            usuarios.Object, Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>(), Sesion(DatosProductos.UsuarioAdmin).Object, Navegacion);
        vm.InicializarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    private static ReporteTareasViewModel TareasVm(ReporteTareasDto reporte)
    {
        var servicio = new Mock<IReporteTareasService>();
        servicio.Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>())).ReturnsAsync(reporte);
        var vm = new ReporteTareasViewModel(servicio.Object, Mock.Of<IPdfExporter>(), Mock.Of<IServicioGuardadoArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), Sesion(DatosProductos.UsuarioAdmin).Object, Navegacion);
        // El constructor pone el año en curso: los datos fijos lo pisan antes de la carga.
        vm.FechaDesde = new DateTime(2026, 9, 1);
        vm.FechaHasta = new DateTime(2026, 10, 31);
        return vm;
    }

    public static ShellMainViewModel Tareas()
    {
        var vm = TareasVm(ReporteZonas);
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    public static ShellMainViewModel TareasCierre()
    {
        var vm = TareasVm(ReporteZonasPorCierre);
        vm.CriterioOpcionSeleccionada = vm.CriteriosDisponibles[1];
        vm.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }
}
