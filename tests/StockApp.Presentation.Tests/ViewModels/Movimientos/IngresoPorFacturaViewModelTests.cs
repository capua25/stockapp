using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class IngresoPorFacturaViewModelTests
{
    private static (IngresoPorFacturaViewModel vm,
                    Mock<IIngresoPorFacturaService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svc = new Mock<IIngresoPorFacturaService>();
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(null, null, null)).ReturnsAsync(new List<ProductoDto>
        {
            new(1, "SKU1", null, "Producto Uno", null, null, null, null, 1, "Unidad", 10m, 20m, 5m, 0m, true, DateTime.UtcNow),
        });
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(new List<Categoria>());
        var unidades = new Mock<IUnidadMedidaService>();
        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(new List<UnidadMedida>
        {
            new() { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true },
        });
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Proveedor Uno", Activo = true },
        });
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Fuente Uno", Activo = true },
        });
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>
        {
            new() { Id = 1, Codigo = 1, Nombre = "Rubro Uno", Activo = true },
        });
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            new Mock<IAuthorizationService>().Object,
            new Mock<ICurrentSession>().Object);

        var vm = new IngresoPorFacturaViewModel(
            svc.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel);

        return (vm, svc, nav);
    }

    private static async Task InicializarYCompletarCabeceraAsync(IngresoPorFacturaViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Compra de insumos";
        vm.MontoTotalTexto = "1.000,00";
    }

    [Fact]
    public async Task AgregarYQuitarRenglones_RecalculaLaSuma()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 3m;
        vm.Renglones[0].PrecioUnitario = 50m;

        Assert.Equal(150m, vm.SumaRenglones);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[1].Cantidad = 2m;
        vm.Renglones[1].PrecioUnitario = 25m;

        Assert.Equal(200m, vm.SumaRenglones);   // 150 + 50

        vm.QuitarRenglonCommand.Execute(vm.Renglones[0]);

        Assert.Equal(50m, vm.SumaRenglones);
    }

    [Fact]
    public async Task CambiarMontoTotalTexto_ActualizaLaDiferencia()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Cantidad = 4m;
        vm.Renglones[0].PrecioUnitario = 100m;   // subtotal 400

        vm.MontoTotalTexto = "450,00";

        Assert.Equal(50m, vm.DiferenciaConTotal);

        vm.MontoTotalTexto = "400,00";

        Assert.Equal(0m, vm.DiferenciaConTotal);
    }

    [Fact]
    public async Task PuedeGuardar_SinRenglones_EsFalse()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        Assert.False(vm.GuardarCommand.CanExecute(null));

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        Assert.True(vm.GuardarCommand.CanExecute(null));
    }
}
