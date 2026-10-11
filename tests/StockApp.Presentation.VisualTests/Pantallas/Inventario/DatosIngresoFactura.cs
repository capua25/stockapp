using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/ingreso-factura*.html (textos fijados en DatosIngresoFacturaTests).
/// Los renglones entran por AgregarArticuloCommand, como los carga el usuario, así suma y diferencia las calcula el VM.
/// Los precios son los de costo de DatosProductos: el guardado no pide confirmación de precios.
/// </summary>
public static class DatosIngresoFactura
{
    public sealed record Renglon(string Producto, decimal Cantidad, decimal Precio, bool ActualizarCosto = false);

    public sealed record Estado(bool ConCabecera, IReadOnlyList<Renglon> Renglones, string? Total,
        bool Credito = false, string? ErrorCarga = null, bool Guardado = false);

    public const string ProductoNuevoNombre = "Carretilla de obra 90 L";
    public const string ProductoNuevoCodigo = "MAT-0601";
    public static readonly DateTime Fecha = new(2026, 10, 6);
    public static readonly DateTime Vencimiento = new(2026, 11, 5);

    public static readonly IReadOnlyList<Proveedor> Proveedores =
    [
        new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true },
        new() { Id = 2, Nombre = "Ferretería Industrial del Este", Activo = true },
    ];

    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "Fondo de Desarrollo del Interior", Activo = true },
        new() { Id = 2, Nombre = "Rentas generales", Activo = true },
    ];

    public static readonly IReadOnlyList<RubroGasto> Rubros =
    [
        new() { Id = 1, Codigo = 231, Nombre = "Materiales de construcción", Activo = true },
    ];

    public static readonly IReadOnlyList<LineaPoa> Lineas =
    [
        new() { Id = 1, Nombre = "Mantenimiento de espacios públicos", Programa = "Obras", Ejercicio = 2026, Activo = true },
    ];

    private static readonly IReadOnlyList<Renglon> Seis =
    [
        new("Cemento portland 50 kg", 40m, 480m, ActualizarCosto: true),
        new("Arena gruesa", 6m, 890m),
        new("Caño PVC 110 mm", 12m, 650m),
        new("Cable 2x1,5 mm", 100m, 38m),
        new("Guantes de trabajo", 20m, 95m),
        new(ProductoNuevoNombre, 2m, 3450m),
    ];

    public static readonly Estado Vacio = new(false, [], null);
    public static readonly Estado ConRenglones = new(true, Seis, "44.940,00");
    public static readonly Estado ConDiferencia = new(true, Seis, "45.000,00", Credito: true,
        ErrorCarga: "La cantidad debe ser mayor a cero.");
    public static readonly Estado Guardado = new(true, Seis, "44.940,00", Guardado: true);

    public static IngresoPorFacturaViewModel Vm()
    {
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(DatosProductos.Productos);
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(DatosProductos.Categorias);
        var unidades = new Mock<IUnidadMedidaService>();
        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(DatosProductos.Unidades);
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(Proveedores);
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes);
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(Rubros);
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(Lineas);

        var servicio = new Mock<IIngresoPorFacturaService>();
        servicio.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(4521, [1, 2, 3, 4, 5, 6], 44940m, 0m));
        var adjuntos = new Mock<IAdjuntoService>();
        adjuntos.Setup(a => a.ListarPorGastoAsync(4521)).ReturnsAsync(
        [
            new AdjuntoDto(1, "factura-A-0004521.pdf", "application/pdf", 182_400, 4521, null,
                new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc)),
        ]);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var panel = new AdjuntosPanelViewModel(adjuntos.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion.Object);

        return new IngresoPorFacturaViewModel(servicio.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object, Mock.Of<INavigationService>(),
            Mock.Of<IConfirmacionService>(), panel,
            Mock.Of<IAltaProductoRapidaDialogService>(), Mock.Of<IConfirmacionPreciosDialogService>());
    }

    public static void Aplicar(IngresoPorFacturaViewModel vm, Estado estado)
    {
        if (vm.ProductosDisponibles.Count == 0 || vm.ProveedoresDisponibles.Count == 0)
            throw new InvalidOperationException("La carga de los combos no corrió antes de aplicar el estado.");

        vm.FechaSeleccionada = Fecha;
        if (estado.ConCabecera)
        {
            vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
            vm.NumeroFactura = "A-0004521";
            vm.NumeroOrden = "OC-2026-118";
            vm.Detalle = "Materiales para la reparación de la rambla";
            vm.Destino = "Corralón municipal";
            vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
            vm.RubroSeleccionado = vm.RubrosDisponibles[0];
            vm.LineaPoaSeleccionada = vm.LineasPoaDisponibles[0];
        }
        vm.EsCredito = estado.Credito;
        vm.FechaVencimientoSeleccionada = estado.Credito ? Vencimiento : null;

        foreach (var r in estado.Renglones)
        {
            if (r.Producto == ProductoNuevoNombre)
            {
                vm.EsProductoNuevoEnCarga = true;
                vm.NuevoProductoCodigo = ProductoNuevoCodigo;
                vm.NuevoProductoNombre = ProductoNuevoNombre;
                vm.NuevaCategoriaSeleccionada = vm.CategoriasDisponibles.Single(c => c.Nombre == "Herramientas");
                vm.NuevaUnidadSeleccionada = vm.UnidadesMedidaDisponibles.Single(u => u.Nombre == "Unidad");
            }
            else
            {
                vm.ProductoEnCarga = vm.ProductosDisponibles.Single(p => p.Nombre == r.Producto);
            }
            vm.CantidadEnCarga = r.Cantidad;
            vm.PrecioUnitarioEnCarga = r.Precio;
            vm.ActualizarPrecioCostoEnCarga = r.ActualizarCosto;
            vm.AgregarArticuloCommand.Execute(null);
        }

        vm.MontoTotalTexto = estado.Total ?? string.Empty;
        vm.MensajeErrorCarga = estado.ErrorCarga;
        if (estado.Guardado)
            vm.GuardarCommand.ExecuteAsync(null).GetAwaiter().GetResult();
    }

    public static (ShellMainViewModel Shell, IngresoPorFacturaViewModel Vm) EnShell()
    {
        var vm = Vm();
        return (DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "IngresoPorFactura", vm), vm);
    }
}
