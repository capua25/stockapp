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
            new(1, "SKU1", null, "Producto Uno", null, null, null, null, 1, "Unidad", 10m, 5m, 0m, true, DateTime.UtcNow),
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
            nav.Object, confirm.Object, adjuntosPanel);

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
            nav.Object, confirm.Object, adjuntosPanel);

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

    /// <summary>
    /// Adaptado al nuevo flujo (encargo "carga por formulario"): el alta en línea ya NO escribe
    /// directo sobre una fila ya agregada a la grilla (AbrirAltaProductoCommand ya no toma un
    /// parámetro FilaRenglonFacturaVm). Ahora "Producto nuevo" dejar la ZONA DE CARGA en modo
    /// producto nuevo (EsProductoNuevoEnCarga), y recién AgregarArticuloCommand crea el renglón.
    /// La garantía que este test custodia sigue siendo la misma: cargar un producto nuevo NO
    /// descarta los renglones ya agregados antes.
    /// </summary>
    [Fact]
    public async Task AltaEnLinea_NoDescartaLosRenglonesYaCargados()
    {
        var (vm, _, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 2m, precioUnitario: 30m);

        vm.AbrirAltaProductoCommand.Execute(null);
        vm.NuevoProductoCodigo = "SKU-INLINE";
        vm.NuevoProductoNombre = "Producto cargado en línea";
        vm.NuevaUnidadSeleccionada = vm.UnidadesMedidaDisponibles[0];
        vm.ConfirmarAltaProductoCommand.Execute(null);

        Assert.True(vm.EsProductoNuevoEnCarga);
        vm.CantidadEnCarga = 1m;
        vm.PrecioUnitarioEnCarga = 40m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Equal(2, vm.Renglones.Count);
        Assert.False(vm.MostrandoAltaProducto);
        // el renglón cargado antes queda intacto
        Assert.Equal(2m, vm.Renglones[0].Cantidad);
        Assert.Equal(30m, vm.Renglones[0].PrecioUnitario);
        // el renglón nuevo queda marcado como producto nuevo
        var filaNueva = vm.Renglones[1];
        Assert.True(filaNueva.EsProductoNuevo);
        Assert.Equal("SKU-INLINE", filaNueva.ProductoNuevoCodigo);
        Assert.Equal("Producto cargado en línea", filaNueva.ProductoNuevoNombre);
        // la zona de carga queda limpia después de agregar (vuelve a modo "producto existente")
        Assert.False(vm.EsProductoNuevoEnCarga);
    }

    [Fact]
    public async Task Guardar_ListaSoloLosProductosCuyoPrecioCostoDifiere()
    {
        var (vm, svc, _) = Crear();
        await InicializarYCompletarCabeceraAsync(vm);

        // Renglon 1: mismo precio que PrecioCosto del producto (10m) → NO entra en la confirmación.
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

        // Renglon 2: precio distinto (15m != 10m) → SÍ entra en la confirmación.
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

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
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        // Renglon 2: precio distinto (20m != 10m), el usuario NO confirma la actualización.
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 20m);

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
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

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
}
