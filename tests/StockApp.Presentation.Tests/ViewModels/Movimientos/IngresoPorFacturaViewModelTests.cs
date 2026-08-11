using Moq;
using StockApp.ApiClient;
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

    [Fact]
    public async Task Guardar_SesionExpirada_MuestraMensajeYNoQuedaInconsistente()
    {
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeError));
        Assert.False(vm.GuardadoExitoso);
        Assert.Null(vm.GastoIdCreado);
    }

    [Fact]
    public async Task Guardar_ServidorNoDisponible_MuestraMensajeAccionableYNoQuedaInconsistente()
    {
        // Fix 5 (revisión final): antes de este fix, ServidorNoDisponibleException no estaba
        // entre los catch de GuardarInternoAsync y, al venir de un AsyncRelayCommand, terminaba
        // en TaskScheduler.UnobservedTaskException (crash.log) sin avisar al operario.
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ThrowsAsync(new ServidorNoDisponibleException());

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, vm.MensajeError);
        Assert.False(vm.GuardadoExitoso);
        Assert.Null(vm.GastoIdCreado);
    }

    [Fact]
    public async Task Guardar_ErrorInesperado_MuestraMensajeGenericoYNoQuedaInconsistente()
    {
        // Red de último recurso agregada junto con el fix de ServidorNoDisponibleException: una
        // excepción no prevista no debe quedar muda.
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeError));
        Assert.False(vm.GuardadoExitoso);
        Assert.Null(vm.GastoIdCreado);
    }

    [Fact]
    public async Task AltaEnLinea_NoDescartaLosRenglonesYaCargados()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 2m;
        vm.Renglones[0].PrecioUnitario = 30m;

        vm.AgregarRenglonCommand.Execute(null);
        var filaNueva = vm.Renglones[1];

        vm.AbrirAltaProductoCommand.Execute(filaNueva);
        vm.NuevoProductoCodigo = "SKU-INLINE";
        vm.NuevoProductoNombre = "Producto cargado en línea";
        vm.NuevaUnidadSeleccionada = vm.UnidadesMedidaDisponibles[0];
        vm.NuevoProductoPrecioVenta = 40m;
        vm.ConfirmarAltaProductoCommand.Execute(null);

        Assert.Equal(2, vm.Renglones.Count);
        Assert.False(vm.MostrandoAltaProducto);
        // el renglón cargado antes queda intacto
        Assert.Equal(2m, vm.Renglones[0].Cantidad);
        Assert.Equal(30m, vm.Renglones[0].PrecioUnitario);
        // el renglón editado queda marcado como producto nuevo
        Assert.True(filaNueva.EsProductoNuevo);
        Assert.Equal("SKU-INLINE", filaNueva.ProductoNuevoCodigo);
        Assert.Equal("Producto cargado en línea", filaNueva.ProductoNuevoNombre);
    }

    [Fact]
    public async Task Guardar_ListaSoloLosProductosCuyoPrecioCostoDifiere()
    {
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        // Renglon 1: mismo precio que PrecioCosto del producto (10m) → NO entra en la confirmación.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        // Renglon 2: precio distinto (15m != 10m) → SÍ entra en la confirmación.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[1].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[1].Cantidad = 1m;
        vm.Renglones[1].PrecioUnitario = 15m;

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.MostrandoConfirmacionPrecios);
        var cambio = Assert.Single(vm.CambiosDePrecio);
        Assert.Equal(10m, cambio.PrecioActual);
        Assert.Equal(15m, cambio.PrecioNuevo);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmarPreciosYGuardar_SoloAplicaLosTildados()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1, 2 }, 35m, 0m));
        await InicializarYCompletarCabeceraAsync(vm);

        // Renglon 1: precio distinto (15m != 10m), el usuario SÍ confirma la actualización.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 15m;

        // Renglon 2: precio distinto (20m != 10m), el usuario NO confirma la actualización.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[1].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[1].Cantidad = 1m;
        vm.Renglones[1].PrecioUnitario = 20m;

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.CambiosDePrecio.Count);
        vm.CambiosDePrecio[0].Confirmado = true;
        vm.CambiosDePrecio[1].Confirmado = false;

        await vm.ConfirmarPreciosYGuardarCommand.ExecuteAsync(null);

        Assert.True(vm.Renglones[0].ActualizarPrecioCosto);
        Assert.False(vm.Renglones[1].ActualizarPrecioCosto);
        svc.Verify(s => s.RegistrarAsync(It.Is<IngresoPorFacturaDto>(
            d => d.Renglones[0].ActualizarPrecioCosto && !d.Renglones[1].ActualizarPrecioCosto)), Times.Once);
    }

    [Fact]
    public async Task Guardar_SinCambiosDePrecio_NoMuestraConfirmacionYGuardaDirecto()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1 }, 10m, 0m));
        await InicializarYCompletarCabeceraAsync(vm);

        // Mismo precio que PrecioCosto del producto (10m) → ningún renglón entra en la confirmación.
        vm.AgregarRenglonCommand.Execute(null);
        vm.Renglones[0].Producto = vm.ProductosDisponibles[0];
        vm.Renglones[0].Cantidad = 1m;
        vm.Renglones[0].PrecioUnitario = 10m;

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(vm.MostrandoConfirmacionPrecios);
        Assert.Empty(vm.CambiosDePrecio);
        Assert.True(vm.GuardadoExitoso);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Once);
    }

    [Fact]
    public void Constructor_ExponeAdjuntosPanel()
    {
        // Smoke test de wiring: confirma que la vista puede bindear vm.AdjuntosPanel.* sin
        // que el VM lo oculte accidentalmente al agregar Task 9's ObservableProperty nuevos.
        var (vm, _, _) = Crear();

        Assert.NotNull(vm.AdjuntosPanel);
    }

    [Fact]
    public void FilaRenglonFacturaVm_NombreMostrado_NotificaAlCambiarProducto()
    {
        var fila = new FilaRenglonFacturaVm();
        var notificados = new List<string?>();
        fila.PropertyChanged += (_, e) => notificados.Add(e.PropertyName);

        fila.Producto = new ProductoDto(1, "SKU1", null, "Producto Uno", null, null, null, null, 1, "Unidad", 10m, 20m, 5m, 0m, true, DateTime.UtcNow);

        Assert.Contains(nameof(FilaRenglonFacturaVm.NombreMostrado), notificados);
    }

    [Fact]
    public void FilaRenglonFacturaVm_NombreMostrado_NotificaAlCambiarEsProductoNuevo()
    {
        var fila = new FilaRenglonFacturaVm();
        var notificados = new List<string?>();
        fila.PropertyChanged += (_, e) => notificados.Add(e.PropertyName);

        fila.EsProductoNuevo = true;

        Assert.Contains(nameof(FilaRenglonFacturaVm.NombreMostrado), notificados);
    }

    [Fact]
    public void FilaRenglonFacturaVm_NombreMostrado_NotificaAlCambiarProductoNuevoNombre()
    {
        var fila = new FilaRenglonFacturaVm();
        var notificados = new List<string?>();
        fila.PropertyChanged += (_, e) => notificados.Add(e.PropertyName);

        fila.ProductoNuevoNombre = "Producto cargado en línea";

        Assert.Contains(nameof(FilaRenglonFacturaVm.NombreMostrado), notificados);
    }
}
