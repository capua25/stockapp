using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Importacion;

/// <summary>
/// Datos fijos de la importación, que sigue en v1 (Fase 8 fuera del rediseño). Alimentan las capturas de referencia de
/// <see cref="ImportacionV1SinCambiosTests"/>: los servicios son Mocks que responden al instante, sin ODS ni red.
/// </summary>
public static class DatosImportacion
{
    // Día mayor a 12 en todas las fechas: la columna Fecha del historial es un binding bidireccional con StringFormat y, con la
    // cultura invariante del test, una fecha como 06/10 se reinterpreta como 10/06 y el binding rebota sin fin (desborda la pila).
    public static readonly IReadOnlyList<ImportacionHistorialDto> Historial =
    [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), new DateTime(2026, 10, 16, 13, 0, 0, DateTimeKind.Unspecified), 2026, "admin", false),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), new DateTime(2026, 9, 15, 9, 30, 0, DateTimeKind.Unspecified), 2026, "ramiro", true),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), new DateTime(2025, 12, 20, 16, 45, 0, DateTimeKind.Unspecified), 2025, "admin", false),
    ];

    private static GastoAnalizadoDto Gasto(int fila, EstadoFila estado, DateOnly? fecha, decimal? monto, string? proveedor, string? factura,
        string? detalle, string? fuente, int? codigoRubro, string? rubro, IReadOnlyList<MotivoEstado>? motivos = null) =>
        new("MARZO", fila, estado, motivos ?? [], fecha, monto, proveedor, false, factura, null, detalle, null, fuente, fuente is null,
            codigoRubro, rubro, false, null);

    private static readonly ResultadoAnalisisDto Analisis = new(
        [new IngresoAnalizadoDto("MARZO", 3, EstadoFila.Ok, [], new DateOnly(2026, 3, 2), 500_000m, "Coparticipación", "Rentas generales", false)],
        [
            Gasto(1, EstadoFila.Ok, new DateOnly(2026, 3, 4), 18_500m, "Ferretería Central", "A-0001", "Materiales de obra", "Rentas generales", 10, "Materiales"),
            Gasto(2, EstadoFila.Advertencia, new DateOnly(2026, 3, 9), 42_000m, "Imprenta del Sur", "B-0230", "Folletería institucional", null, 20, "Servicios",
                [new MotivoEstado(TipoMotivo.LiteralVacio, "Fuente sin identificar")]),
            Gasto(3, EstadoFila.Error, null, 9_800m, "Librería Norte", "C-0045", "Útiles de oficina", "Rentas generales", 10, "Materiales",
                [new MotivoEstado(TipoMotivo.FechaIlegible, "Fecha ilegible")]),
        ],
        [new LineaPoaAnalizadaDto("Mantenimiento de espacios públicos", 2026, true, EstadoFila.Ok, [], "OBRAS", false, 1_200_000m, 632_400m, [])],
        new MaestrosNuevosDto(["Imprenta del Sur"], [], []),
        new ResumenAnalisisDto(3, 1, 1, 1, 0, 0, 0),
        new SaldosTotalesPoaOds(0m, 0m));

    private static Mock<IImportacionService> Servicio()
    {
        var servicio = new Mock<IImportacionService>();
        servicio.Setup(s => s.AnalizarAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(Analisis);
        servicio.Setup(s => s.ListarHistorialAsync()).ReturnsAsync(Historial);
        return servicio;
    }

    private static NuevaImportacionViewModel Nueva(Mock<IImportacionService> servicio)
    {
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        seleccion.Setup(s => s.SeleccionarArchivoOdsAsync()).ReturnsAsync(("planilla.ods", new byte[] { 1 }));
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(s => s.ListarActivasAsync()).ReturnsAsync([new FuenteFinanciamiento { Id = 1, Nombre = "Rentas generales" }]);
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(s => s.ListarActivosAsync()).ReturnsAsync([new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales" }, new RubroGasto { Id = 2, Codigo = 20, Nombre = "Servicios" }]);
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(s => s.ListarTodosAsync()).ReturnsAsync([new Proveedor { Id = 1, Nombre = "Ferretería Central" }, new Proveedor { Id = 2, Nombre = "Librería Norte" }]);
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(s => s.ListarTodasAsync()).ReturnsAsync([]);
        return new NuevaImportacionViewModel(servicio.Object, seleccion.Object, Mock.Of<IConfirmacionService>(), fuentes.Object, rubros.Object,
            proveedores.Object, lineas.Object);
    }

    /// <summary>El asistente recién abierto: paso de selección de archivos.</summary>
    public static NuevaImportacionViewModel NuevaEnSeleccion() => Nueva(Servicio());

    /// <summary>El asistente en el paso de revisión: un gasto correcto con celdas con candado, uno con advertencia, uno con
    /// error de lectura y el último con un error devuelto por el servidor.</summary>
    public static NuevaImportacionViewModel NuevaEnRevision()
    {
        var vm = Nueva(Servicio());
        vm.SeleccionarGastosCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.SeleccionarPoaCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.AnalizarCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        vm.FilasGasto[0].AgregarErrorServidor("La factura ya existe para este proveedor.");
        return vm;
    }

    public static HistorialImportacionesViewModel HistorialConTresFilas() =>
        new(Servicio().Object, Mock.Of<IConfirmacionService>());

    public static ImportacionViewModel Importacion() => new(NuevaEnSeleccion(), HistorialConTresFilas());
}
