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
        var (vm, svc, nav, _, _) = CrearConDialogos();
        return (vm, svc, nav);
    }

    /// <summary>
    /// Los diálogos de alta y de precios se piden a servicios (convención 25): por defecto los dos devuelven null
    /// (cancelan). Un test que necesita otra respuesta hace Setup sobre el mock que devuelve.
    /// </summary>
    private static (IngresoPorFacturaViewModel vm,
                    Mock<IIngresoPorFacturaService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IAltaProductoRapidaDialogService> altaMock,
                    Mock<IConfirmacionPreciosDialogService> preciosMock)
        CrearConDialogos()
    {
        var svc = new Mock<IIngresoPorFacturaService>();
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(null, null, null)).ReturnsAsync(new List<ProductoDto>
        {
            new(1, "SKU1", null, "Producto Uno", null, null, null, null, 1, "Unidad", 10m, 5m, 0m, true, DateTime.UtcNow),
        });
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(new List<Categoria> { new() { Id = 3, Nombre = "Herramientas", Activo = true } });
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
        // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15, mismo criterio que
        // GastosViewModel/GastoFormViewModel): el servidor ya filtra a Activo=true, sin filtro
        // repetido acá.
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(new List<Proveedor>
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

        var alta = new Mock<IAltaProductoRapidaDialogService>();
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync((ProductoNuevoDto?)null);
        var precios = new Mock<IConfirmacionPreciosDialogService>();
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()))
            .ReturnsAsync((IReadOnlyList<bool>?)null);

        var vm = new IngresoPorFacturaViewModel(
            svc.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel, alta.Object, precios.Object);

        return (vm, svc, nav, alta, precios);
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

    /// <summary>
    /// La edición inline de renglones desapareció (encargo "carga por formulario"): la única vía
    /// de alta es la zona de carga (ProductoEnCarga/CantidadEnCarga/PrecioUnitarioEnCarga +
    /// AgregarArticuloCommand). Este helper reemplaza el viejo patrón
    /// "AgregarRenglonCommand.Execute(null); Renglones[i].Cantidad = ..." de todos los tests de
    /// abajo que solo necesitan un renglón cargado, sin repetir las 4 líneas en cada uno.
    /// </summary>
    private static void AgregarArticulo(IngresoPorFacturaViewModel vm, ProductoDto producto, decimal cantidad, decimal precioUnitario)
    {
        vm.ProductoEnCarga = producto;
        vm.CantidadEnCarga = cantidad;
        vm.PrecioUnitarioEnCarga = precioUnitario;
        vm.AgregarArticuloCommand.Execute(null);
    }

    // ── AgregarArticuloCommand: valida ANTES de insertar, mismas reglas que
    // IngresoPorFacturaService.RegistrarAsync (cantidad > 0, precio >= 0, producto existente o
    // producto nuevo). La cobertura fuerte de estos caminos vive en IngresoPorFacturaViewTests.cs
    // (UiTests, clicks reales); estos tests unitarios son una segunda red de contención directa
    // sobre el VM, útil para mutación rápida sin levantar Avalonia headless.

    [Fact]
    public async Task AgregarArticulo_SinProductoNiProductoNuevo_NoAgregaYMuestraMensajeEspecifico()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.CantidadEnCarga = 1m;
        vm.PrecioUnitarioEnCarga = 10m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Empty(vm.Renglones);
        Assert.Equal("Debe seleccionar un producto o cargar uno nuevo.", vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task AgregarArticulo_CantidadCero_NoAgregaYMuestraMensajeEspecifico()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        vm.CantidadEnCarga = 0m;
        vm.PrecioUnitarioEnCarga = 10m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Empty(vm.Renglones);
        Assert.Equal("La cantidad debe ser mayor a cero.", vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task AgregarArticulo_CantidadNegativa_NoAgregaYMuestraMensajeEspecifico()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        vm.CantidadEnCarga = -3m;
        vm.PrecioUnitarioEnCarga = 10m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Empty(vm.Renglones);
        Assert.Equal("La cantidad debe ser mayor a cero.", vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task AgregarArticulo_PrecioNegativo_NoAgregaYMuestraMensajeEspecifico()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        vm.CantidadEnCarga = 1m;
        vm.PrecioUnitarioEnCarga = -5m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Empty(vm.Renglones);
        Assert.Equal("El precio unitario no puede ser negativo.", vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task AgregarArticulo_ConExito_LimpiaLaZonaDeCargaYPideElFoco()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        vm.ActualizarPrecioCostoEnCarga = true;

        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 2m, precioUnitario: 10m);

        Assert.Null(vm.ProductoEnCarga);
        Assert.Equal(0m, vm.CantidadEnCarga);
        Assert.Equal(0m, vm.PrecioUnitarioEnCarga);
        Assert.False(vm.ActualizarPrecioCostoEnCarga);
        Assert.Null(vm.MensajeErrorCarga);
        Assert.True(vm.SolicitarFocoEnProductoCombo);
    }

    [Fact]
    public async Task AgregarArticulo_PrecioCero_EsValido_SeAgrega()
    {
        // El límite de la regla del service es "PrecioUnitario < 0", NO "<= 0" -- precio 0 es
        // válido (ej. donaciones/promociones). Mismo límite exacto que
        // IngresoPorFacturaService.RegistrarAsync:69-79.
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 0m);

        Assert.Single(vm.Renglones);
        Assert.Null(vm.MensajeErrorCarga);
    }

    // ── bugfix 2026-08-15: mismo bug que GastoFormViewModel — InicializarAsync llamaba
    // IProveedorService.ListarTodosAsync(), que en el servidor exige GestionarTablasMaestras.
    // Esta pantalla la alcanza un Operador con RegistrarMovimientos + RegistrarGastos +
    // VerFinanzas (ShellMainViewModel.PuedeIngresarPorFactura), que no necesariamente tiene
    // GestionarTablasMaestras.

    [Fact]
    public async Task InicializarAsync_ConsultaProveedoresActivos_NoTodosLosProveedores()
    {
        var svc = new Mock<IIngresoPorFacturaService>();
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(null, null, null)).ReturnsAsync(new List<ProductoDto>());
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(new List<Categoria>());
        var unidades = new Mock<IUnidadMedidaService>();
        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(new List<UnidadMedida>());
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(new List<Proveedor>());
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            new Mock<ICurrentSession>().Object);

        var vm = new IngresoPorFacturaViewModel(
            svc.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel,
            Mock.Of<IAltaProductoRapidaDialogService>(), Mock.Of<IConfirmacionPreciosDialogService>());

        await vm.InicializarAsync();

        proveedores.Verify(p => p.ListarActivasAsync(), Times.Once);
        proveedores.Verify(p => p.ListarTodosAsync(), Times.Never);
    }

    [Fact]
    public async Task InicializarAsync_SiProveedoresLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svc = new Mock<IIngresoPorFacturaService>();
        var productos = new Mock<IProductoService>();
        var categorias = new Mock<ICategoriaService>();
        var unidades = new Mock<IUnidadMedidaService>();
        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        var rubros = new Mock<IRubroGastoService>();
        var lineas = new Mock<ILineaPoaService>();
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        var adjuntosPanel = new AdjuntosPanelViewModel(
            new Mock<IAdjuntoService>().Object,
            new Mock<IServicioSeleccionArchivo>().Object,
            new Mock<IServicioAperturaArchivo>().Object,
            confirm.Object,
            new Mock<ICurrentSession>().Object);

        var vm = new IngresoPorFacturaViewModel(
            svc.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel,
            Mock.Of<IAltaProductoRapidaDialogService>(), Mock.Of<IConfirmacionPreciosDialogService>());

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
    }

    [Fact]
    public async Task AgregarYQuitarRenglones_RecalculaLaSuma()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 3m, precioUnitario: 50m);

        Assert.Equal(150m, vm.SumaRenglones);

        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 2m, precioUnitario: 25m);

        Assert.Equal(200m, vm.SumaRenglones);   // 150 + 50

        vm.QuitarRenglonCommand.Execute(vm.Renglones[0]);

        Assert.Equal(50m, vm.SumaRenglones);
    }

    [Fact]
    public async Task CambiarMontoTotalTexto_ActualizaLaDiferencia()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 4m, precioUnitario: 100m);   // subtotal 400

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

        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_SesionExpirada_MuestraMensajeYNoQuedaInconsistente()
    {
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

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
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

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
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeError));
        Assert.False(vm.GuardadoExitoso);
        Assert.Null(vm.GastoIdCreado);
    }

    [Fact]
    public async Task AbrirAltaProducto_PasaLasCategoriasYUnidadesYaCargadas()
    {
        var (vm, _, _, alta, _) = CrearConDialogos();
        await vm.InicializarAsync();

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);

        alta.Verify(a => a.PedirProductoNuevoAsync(
            It.Is<IReadOnlyList<Categoria>>(c => c.SequenceEqual(vm.CategoriasDisponibles)),
            It.Is<IReadOnlyList<UnidadMedida>>(u => u.SequenceEqual(vm.UnidadesMedidaDisponibles))), Times.Once);
    }

    [Fact]
    public async Task AbrirAltaProducto_Cancelada_LaZonaDeCargaQuedaIntacta()
    {
        var (vm, _, _, _, _) = CrearConDialogos();
        await vm.InicializarAsync();
        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        vm.CantidadEnCarga = 3m;
        vm.PrecioUnitarioEnCarga = 12m;

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);

        Assert.Same(vm.ProductosDisponibles[0], vm.ProductoEnCarga);
        Assert.False(vm.EsProductoNuevoEnCarga);
        Assert.Null(vm.NuevoProductoCodigo);
        Assert.Equal(3m, vm.CantidadEnCarga);
        Assert.False(vm.SolicitarFocoEnCantidad);
    }

    [Fact]
    public async Task AbrirAltaProducto_Confirmada_QuedaComoProductoNuevoEnCargaYPideElFocoEnCantidad()
    {
        var (vm, _, _, alta, _) = CrearConDialogos();
        await vm.InicializarAsync();
        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync(new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1));

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);

        Assert.True(vm.EsProductoNuevoEnCarga);
        Assert.Null(vm.ProductoEnCarga);
        Assert.Equal("MAT-0601", vm.NuevoProductoCodigo);
        Assert.Equal("Carretilla de obra 90 L", vm.NuevoProductoNombre);
        Assert.Equal(3, vm.NuevaCategoriaSeleccionada!.Id);
        Assert.Equal(1, vm.NuevaUnidadSeleccionada!.Id);
        Assert.True(vm.SolicitarFocoEnCantidad);
    }

    [Fact]
    public async Task AltaEnLinea_NoDescartaLosRenglonesYaCargados()
    {
        var (vm, _, _, alta, _) = CrearConDialogos();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 2m, precioUnitario: 30m);
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync(new ProductoNuevoDto("SKU-INLINE", "Producto cargado en línea", null, 1));

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);
        vm.CantidadEnCarga = 1m;
        vm.PrecioUnitarioEnCarga = 40m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Equal(2, vm.Renglones.Count);
        Assert.Equal(2m, vm.Renglones[0].Cantidad);
        Assert.Equal(30m, vm.Renglones[0].PrecioUnitario);
        var filaNueva = vm.Renglones[1];
        Assert.True(filaNueva.EsProductoNuevo);
        Assert.Equal("SKU-INLINE", filaNueva.ProductoNuevoCodigo);
        Assert.Equal("Producto cargado en línea", filaNueva.ProductoNuevoNombre);
        Assert.Null(filaNueva.ProductoNuevoCategoriaId);
        Assert.Equal(1, filaNueva.ProductoNuevoUnidadMedidaId);
        Assert.False(vm.EsProductoNuevoEnCarga);
    }

    [Fact]
    public async Task Guardar_ListaSoloLosProductosCuyoPrecioCostoDifiere()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        precios.Verify(p => p.ConfirmarCambiosAsync(It.Is<IReadOnlyList<ItemConfirmacionPrecioVm>>(
            l => l.Count == 1 && l[0].PrecioActual == 10m && l[0].PrecioNuevo == 15m)), Times.Once);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_PreciosConfirmados_AplicaLosTildesYGuarda()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1, 2 }, 35m, 0m));
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()))
            .ReturnsAsync(new[] { true, false });
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 20m);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.Renglones[0].ActualizarPrecioCosto);
        Assert.False(vm.Renglones[1].ActualizarPrecioCosto);
        Assert.True(vm.CambiosDePrecio[0].Confirmado);
        svc.Verify(s => s.RegistrarAsync(It.Is<IngresoPorFacturaDto>(
            d => d.Renglones[0].ActualizarPrecioCosto && !d.Renglones[1].ActualizarPrecioCosto)), Times.Once);
        Assert.True(vm.GuardadoExitoso);
    }

    [Fact]
    public async Task Guardar_PreciosCancelados_NoGuardaYConservaTodo()
    {
        var (vm, svc, _, _, _) = CrearConDialogos();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
        Assert.False(vm.GuardadoExitoso);
        Assert.Single(vm.Renglones);
        Assert.Null(vm.MensajeError);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_SinCambiosDePrecio_NoPreguntaYGuardaDirecto()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1 }, 10m, 0m));
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

        await vm.GuardarCommand.ExecuteAsync(null);

        precios.Verify(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()), Times.Never);
        Assert.True(vm.GuardadoExitoso);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Once);
    }

    [Fact]
    public async Task Guardar_MientrasLaConfirmacionEstaAbierta_NoSePuedeVolverAGuardar()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1 }, 15m, 0m));
        var abierta = new TaskCompletionSource<IReadOnlyList<bool>?>();
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>())).Returns(abierta.Task);
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        var primero = vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(vm.GuardarCommand.CanExecute(null), "Con la confirmación abierta, Guardar sigue habilitado.");
        abierta.SetResult(new[] { true });
        await primero;
        precios.Verify(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()), Times.Once);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Once);
    }

    [Fact]
    public async Task Guardar_ProductoNuevoYCambioDePrecio_ConfirmaSoloElExistenteYElDtoLlevaLosDos()
    {
        var (vm, svc, _, alta, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1, 2 }, 95m, 0m));
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync(new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1));
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()))
            .ReturnsAsync(new[] { true });
        await InicializarYCompletarCabeceraAsync(vm);
        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);
        vm.CantidadEnCarga = 2m;
        vm.PrecioUnitarioEnCarga = 40m;
        vm.AgregarArticuloCommand.Execute(null);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        precios.Verify(p => p.ConfirmarCambiosAsync(It.Is<IReadOnlyList<ItemConfirmacionPrecioVm>>(
            l => l.Count == 1 && l[0].ProductoNombre == "Producto Uno")), Times.Once);
        svc.Verify(s => s.RegistrarAsync(It.Is<IngresoPorFacturaDto>(d =>
            d.Renglones.Count == 2
            && d.Renglones[0].ProductoNuevo == new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1)
            && !d.Renglones[0].ActualizarPrecioCosto
            && d.Renglones[1].ProductoId == 1 && d.Renglones[1].ActualizarPrecioCosto)), Times.Once);
    }

    [Fact]
    public async Task Guardar_ServidorCaidoTrasConfirmar_MuestraElErrorYConservaTodo()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>())).ThrowsAsync(new ServidorNoDisponibleException());
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>())).ReturnsAsync(new[] { true });
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, vm.MensajeError);
        Assert.False(vm.GuardadoExitoso);
        Assert.Null(vm.GastoIdCreado);
        Assert.Single(vm.Renglones);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_403TrasConfirmar_MuestraElMensajeDeSesion()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>())).ThrowsAsync(new UnauthorizedAccessException("403"));
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>())).ReturnsAsync(new[] { false });
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("La sesión expiró o no tiene permiso para registrar esta factura. Vuelva a iniciar sesión e intente de nuevo.",
            vm.MensajeError);
        Assert.False(vm.GuardadoExitoso);
        Assert.Single(vm.Renglones);
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

        fila.Producto = new ProductoDto(1, "SKU1", null, "Producto Uno", null, null, null, null, 1, "Unidad", 10m, 5m, 0m, true, DateTime.UtcNow);

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

    // ── Formato es-UY (decisión 2026-10-01): MontoTotalTexto con parseo seguro ──

    [Fact]
    public async Task Guardar_MontoTotalConPuntoDecimal_SeRechazaConMensajeClaroSinRegistrar()
    {
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);
        vm.MontoTotalTexto = "5.4";   // con NumberStyles.Number + es-UY se leía como 54

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("El monto total no es un número válido. Usá coma para los decimales: 5,4", vm.MensajeError);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
    }

    [Fact]
    public async Task CambiarMontoTotalTexto_ConMilesConPunto_CalculaLaDiferenciaCorrecta()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 10m, precioUnitario: 150m);   // subtotal 1500

        vm.MontoTotalTexto = "1.500,50";

        Assert.Equal(0.50m, vm.DiferenciaConTotal);
    }

    [Fact]
    public async Task CambiarMontoTotalTexto_PuntoDecimal_NoSeLeeComoMiles()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 5m);

        vm.MontoTotalTexto = "5.4";   // inválido: antes daba diferencia 49 (54 - 5)

        Assert.Equal(-5m, vm.DiferenciaConTotal);
    }
}
