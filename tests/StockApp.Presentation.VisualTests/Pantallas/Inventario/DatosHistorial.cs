using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/historial*.html: doce movimientos de un depósito municipal, con una
/// salida que deja el stock en negativo, un comentario largo, salidas y ajustes sin precio, y dos usuarios.
/// </summary>
public static class DatosHistorial
{
    public const string ComentarioLargo =
        "Reparación del alumbrado de la plaza Independencia y del pasaje peatonal frente a la escuela 12.";
    public const string ProductoLargo = "Caño de PVC reforzado de 110 mm por 4 metros con junta elástica";
    public const string UsuarioLargo = "Operador de Depósito Central de Obras Públicas";

    /// <summary>Hora LOCAL fija de 2026 convertida a UTC: la celda (UTC a local) muestra lo mismo en cualquier huso.</summary>
    public static DateTime Fecha(int mes, int dia, int hora, int minuto) =>
        new DateTime(2026, mes, dia, hora, minuto, 0, DateTimeKind.Local).ToUniversalTime();

    private const string Maria = "María Pérez";
    private const string Deposito = "Operador de Depósito Central";

    private static MovimientoHistorialDto M(int id, int productoId, string producto, TipoMovimiento tipo, MotivoMovimiento motivo,
        decimal cantidad, decimal precio, decimal anterior, decimal nuevo, string? comentario, DateTime fecha, int usuarioId, string usuario) =>
        new(id, productoId, producto, tipo, motivo, cantidad, precio, anterior, nuevo, comentario, fecha, usuarioId, usuario);

    public static readonly IReadOnlyList<MovimientoHistorialDto> Movimientos =
    [
        M(112, 1, "Cemento portland 50 kg", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 40m, 480m, 280m, 320m, "Remito 0042-00001234", Fecha(10, 7, 14, 35), 1, Maria),
        M(111, 7, "Cable 2x1,5 mm", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 25.5m, 0m, 1275.5m, 1250m, "Alumbrado de la plaza", Fecha(10, 6, 9, 10), 2, Deposito),
        M(110, 3, "Caño PVC 110 mm", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 12m, 650m, 0m, 12m, null, Fecha(10, 3, 11, 0), 1, Maria),
        M(109, 4, "Pintura látex blanca 20 L", TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, 2m, 0m, 4m, 6m, "Diferencia de conteo del inventario de setiembre.", Fecha(10, 1, 16, 45), 1, Maria),
        M(108, 8, "Hipoclorito de sodio 5 L", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 5m, 0m, 2m, -3m, "Limpieza del gimnasio municipal", Fecha(9, 30, 8, 20), 2, Deposito),
        M(107, 5, "Guantes de trabajo", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 100m, 95m, 80m, 180m, null, Fecha(9, 26, 10, 15), 1, Maria),
        M(106, 6, "Lámpara LED 30 W", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 4m, 0m, 12m, 8m, ComentarioLargo, Fecha(9, 24, 15, 5), 2, Deposito),
        M(105, 9, "Bolsas de residuo 80x110", TipoMovimiento.Salida, MotivoMovimiento.Merma, 10m, 0m, 70m, 60m, "Bolsas rotas en el depósito", Fecha(9, 19, 12, 30), 2, Deposito),
        M(104, 11, "Pintura vial amarilla 4 L", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 22.5m, 1890.5m, 0m, 22.5m, null, Fecha(9, 15, 9, 0), 1, Maria),
        M(103, 2, "Arena gruesa", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 45m, 890m, 0m, 45m, "Remito 0042-00001190", Fecha(9, 10, 13, 40), 1, Maria),
        M(102, 10, "Pala de punta", TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, 2m, 0m, 12m, 14m, null, Fecha(9, 4, 11, 25), 1, Maria),
        M(101, 12, "Carretilla reforzada", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 1m, 3200m, 0m, 1m, null, Fecha(9, 1, 8, 0), 1, Maria),
    ];

    /// <summary>Fila 3 (índice 2) con un producto y un usuario más largos que sus columnas.</summary>
    public static readonly IReadOnlyList<MovimientoHistorialDto> MovimientosConTextosLargos =
        Movimientos.Select((m, i) => i == 2 ? m with { ProductoNombre = ProductoLargo, UsuarioNombre = UsuarioLargo } : m).ToList();

    public static MovimientoHistorialViewModel Listado(Func<IReadOnlyList<MovimientoHistorialDto>>? responder = null,
        Task<IReadOnlyList<MovimientoHistorialDto>>? pendiente = null, Exception? falla = null)
    {
        var servicio = new Mock<IMovimientoStockService>();
        var historial = servicio.Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()));
        if (falla is not null)
            historial.ThrowsAsync(falla);
        else if (pendiente is not null)
            historial.Returns(pendiente);
        else
            historial.ReturnsAsync(() => (responder ?? (() => []))());
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new MovimientoHistorialViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IProductoService>(),
            Mock.Of<IConfirmacionService>(), sesion.Object);
    }

    private static ShellMainViewModel EnShell(ViewModelBase contenido) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "HistorialMovimientos", contenido);

    public static ShellMainViewModel ConMovimientos() => EnShell(Listado(() => Movimientos));
    public static ShellMainViewModel SinMovimientos() => EnShell(Listado(() => []));
    public static ShellMainViewModel Cargando() => EnShell(Listado(pendiente: new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>().Task));
    public static ShellMainViewModel SinPermiso() => EnShell(Listado(falla: new UnauthorizedAccessException("403")));
    public static ShellMainViewModel ConCeldaCortada() => EnShell(Listado(() => MovimientosConTextosLargos));
}
