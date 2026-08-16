using System.Threading.Tasks;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ShellMainViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ShellMainViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(RolUsuario rol)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(rol);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"), confirmMock.Object,
            Mock.Of<IAuthService>());
        return (vm, sessionMock, navMock, confirmMock);
    }

    // ── Inicio ───────────────────────────────────────────────────────────────

    [Fact]
    public void NavInicio_LlamaNavegar_AInicioViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavInicioCommand.Execute(null);

        navMock.Verify(n => n.Navegar<InicioViewModel>(), Times.Once);
    }

    [Fact]
    public void NavInicio_EstableceSeccionActiva_Inicio()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavInicioCommand.Execute(null);

        Assert.Equal("Inicio", vm.SeccionActiva);
    }

    // ── D2.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Admin_EsAdmin_True()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        Assert.True(vm.EsAdmin);
    }

    [Fact]
    public void Operador_EsAdmin_False()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        Assert.False(vm.EsAdmin);
    }

    // ── tests: versión de la app ─────────────────────────────────────────────

    [Fact]
    public void VersionTexto_ExponeVersionDeIInfoApp_ConPrefijoV()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var infoApp = Mock.Of<IInfoApp>(x => x.Version == "9.9.9");

        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), infoApp, Mock.Of<IConfirmacionService>(),
            Mock.Of<IAuthService>());

        Assert.Equal("v9.9.9", vm.VersionTexto);
    }

    [Fact]
    public void NavProductos_LlamaNavegar_AProductoListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavProductosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavCategoria_Admin_LlamaNavegar_ACategoriaListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavCategoriasCommand.Execute(null);

        navMock.Verify(n => n.Navegar<CategoriaListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavProveedores_Admin_LlamaNavegar_AProveedorListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavProveedoresCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProveedorListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavUnidadesMedida_Admin_LlamaNavegar_AUnidadMedidaListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavUnidadesMedidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<UnidadMedidaListViewModel>(), Times.Once);
    }

    // ── D6 — Navegación a movimientos ────────────────────────────────────────

    [Fact]
    public void NavRegistrarEntrada_LlamaNavegar_AEntradaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarEntradaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<EntradaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavRegistrarSalida_LlamaNavegar_ASalidaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarSalidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<SalidaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavHistorialMovimientos_LlamaNavegar_AMovimientoHistorialViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavHistorialMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public void NavRegistrarEntrada_Admin_LlamaNavegar_AEntradaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavRegistrarEntradaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<EntradaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavRegistrarSalida_Admin_LlamaNavegar_ASalidaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavRegistrarSalidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<SalidaRegistroViewModel>(), Times.Once);
    }

    // ── Tarea 4 (UI Kit): estado activo del sidebar ──────────────────────────

    [Fact]
    public void NavProductos_EstableceSeccionActiva_Productos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavProductosCommand.Execute(null);

        Assert.Equal("Productos", vm.SeccionActiva);
    }

    [Fact]
    public void NavCategorias_EstableceSeccionActiva_Categorias()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavCategoriasCommand.Execute(null);

        Assert.Equal("Categorias", vm.SeccionActiva);
    }

    [Fact]
    public void NavProveedores_EstableceSeccionActiva_Proveedores()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavProveedoresCommand.Execute(null);

        Assert.Equal("Proveedores", vm.SeccionActiva);
    }

    [Fact]
    public void NavUnidadesMedida_EstableceSeccionActiva_UnidadesMedida()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavUnidadesMedidaCommand.Execute(null);

        Assert.Equal("UnidadesMedida", vm.SeccionActiva);
    }

    [Fact]
    public void NavRegistrarEntrada_EstableceSeccionActiva_RegistrarEntrada()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarEntradaCommand.Execute(null);

        Assert.Equal("RegistrarEntrada", vm.SeccionActiva);
    }

    [Fact]
    public void NavRegistrarSalida_EstableceSeccionActiva_RegistrarSalida()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarSalidaCommand.Execute(null);

        Assert.Equal("RegistrarSalida", vm.SeccionActiva);
    }

    [Fact]
    public void NavHistorialMovimientos_EstableceSeccionActiva_HistorialMovimientos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavHistorialMovimientosCommand.Execute(null);

        Assert.Equal("HistorialMovimientos", vm.SeccionActiva);
    }

    [Fact]
    public void NavValorizacion_EstableceSeccionActiva_Valorizacion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavValorizacionCommand.Execute(null);

        Assert.Equal("Valorizacion", vm.SeccionActiva);
    }

    [Fact]
    public void NavStockCategoria_EstableceSeccionActiva_StockCategoria()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavStockCategoriaCommand.Execute(null);

        Assert.Equal("StockCategoria", vm.SeccionActiva);
    }

    [Fact]
    public void NavHistorialPorProducto_EstableceSeccionActiva_HistorialPorProducto()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavHistorialPorProductoCommand.Execute(null);

        Assert.Equal("HistorialPorProducto", vm.SeccionActiva);
    }

    [Fact]
    public void NavMasMovidos_EstableceSeccionActiva_MasMovidos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavMasMovidosCommand.Execute(null);

        Assert.Equal("MasMovidos", vm.SeccionActiva);
    }

    [Fact]
    public void NavAuditoriaLog_EstableceSeccionActiva_AuditoriaLog()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavAuditoriaLogCommand.Execute(null);

        Assert.Equal("AuditoriaLog", vm.SeccionActiva);
    }

    // ── Cerrar sesión ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CerrarSesionCommand_Confirmado_LimpiaLaSesionYDisparaElEvento()
    {
        var (vm, sessionMock, _, confirmMock) = Crear(RolUsuario.Admin);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var disparado = false;
        vm.CerrarSesionSolicitado += () => disparado = true;

        await vm.CerrarSesionCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("¿Cerrar la sesión?"), Times.Once);
        sessionMock.Verify(s => s.CerrarSesion(), Times.Once);
        Assert.True(disparado);
    }

    [Fact]
    public async Task CerrarSesionCommand_Cancelado_NoLimpiaNiDisparaElEvento()
    {
        var (vm, sessionMock, _, confirmMock) = Crear(RolUsuario.Admin);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        var disparado = false;
        vm.CerrarSesionSolicitado += () => disparado = true;

        await vm.CerrarSesionCommand.ExecuteAsync(null);

        sessionMock.Verify(s => s.CerrarSesion(), Times.Never);
        Assert.False(disparado);
    }

    // ── Desconectar (fix leak de suscripción a INavigationService.Cambiado) ────

    [Fact]
    public void Desconectar_DesuscribeDeNavegacionCambiada_YaNoActualizaCurrentContent()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        var otroVm = Mock.Of<ViewModelBase>();
        navMock.Setup(n => n.Actual).Returns(otroVm);
        navMock.Raise(n => n.Cambiado += null);
        Assert.Same(otroVm, vm.CurrentContent);

        vm.Desconectar();

        var otroVm2 = Mock.Of<ViewModelBase>();
        navMock.Setup(n => n.Actual).Returns(otroVm2);
        navMock.Raise(n => n.Cambiado += null);

        // Tras Desconectar(), el handler ya no está enganchado: CurrentContent no debe
        // actualizarse con la nueva notificación del singleton (INavigationService), que
        // es justamente lo que evita que esta instancia "muerta" quede reaccionando
        // indefinidamente a navegaciones de la sesión siguiente.
        Assert.Same(otroVm, vm.CurrentContent);
    }

    [Fact]
    public void Desconectar_LlamadoDosVeces_NoLanzaExcepcion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.Desconectar();
        var ex = Record.Exception(() => vm.Desconectar());

        Assert.Null(ex);
    }

    // ── F5d Task 10 — Navegación a Importar planillas ────────────────────────

    [Fact]
    public void NavImportacion_LlamaNavegar_AImportacionViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        navMock.Verify(n => n.Navegar<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>(), Times.Once);
    }

    [Fact]
    public void NavImportacion_EstableceSeccionActiva_Importacion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        Assert.Equal("Importacion", vm.SeccionActiva);
    }

    // ── Refresco de permisos al navegar (spec decisión 7) ──────────────────────
    // Pre-flight (mismo riesgo que el bug crítico de Task 13, checkboxes congelados): las
    // propiedades Puede* son getters calculados sobre ICurrentSession.PermisosActuales, que
    // no implementa INotifyPropertyChanged. Si OnNavegacionCambiada solo dispara el refresco
    // sin notificar después, el menú queda mostrando los permisos viejos hasta la próxima
    // navegación aunque el cache subyacente ya haya cambiado.

    [Fact]
    public async Task Navegacion_RefrescaPermisos_YNotificaLasPropiedadesPuede()
    {
        var permisos = new HashSet<string>();
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(() => permisos);

        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync(() =>
        {
            permisos.Add(Permisos.VerReportes);
            return (IReadOnlySet<string>)permisos;
        });

        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), authMock.Object);

        Assert.False(vm.PuedeVerReportes);

        var propiedadesNotificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => propiedadesNotificadas.Add(e.PropertyName);

        navMock.Raise(n => n.Cambiado += null);
        await vm._tareaRefrescoPermisos;

        authMock.Verify(a => a.ObtenerPermisosPropiosAsync(), Times.Once);
        Assert.Contains(nameof(ShellMainViewModel.PuedeVerReportes), propiedadesNotificadas);
        Assert.True(vm.PuedeVerReportes);
    }

    [Fact]
    public async Task Navegacion_RefrescaPermisos_NotificaPuedeGestionarDocumentos()
    {
        var permisos = new HashSet<string>();
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(() => permisos);

        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync(() =>
        {
            permisos.Add(Permisos.GestionarDocumentos);
            return (IReadOnlySet<string>)permisos;
        });

        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), authMock.Object);

        Assert.False(vm.PuedeGestionarDocumentos);

        var propiedadesNotificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => propiedadesNotificadas.Add(e.PropertyName);

        navMock.Raise(n => n.Cambiado += null);
        await vm._tareaRefrescoPermisos;

        authMock.Verify(a => a.ObtenerPermisosPropiosAsync(), Times.Once);
        Assert.Contains(nameof(ShellMainViewModel.PuedeGestionarDocumentos), propiedadesNotificadas);
        Assert.True(vm.PuedeGestionarDocumentos);
    }

    [Fact]
    public async Task Navegacion_SiElRefrescoFalla_NoRompeYElTaskNuncaLanza()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());

        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync())
            .ThrowsAsync(new InvalidOperationException("API caída"));

        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), authMock.Object);

        navMock.Raise(n => n.Cambiado += null);

        var ex = await Record.ExceptionAsync(() => vm._tareaRefrescoPermisos);

        Assert.Null(ex);
    }

    // ── Gating por permiso configurable (spec 2026-08-10) ─────────────────────

    [Theory]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarProductos), Permisos.GestionarProductos)]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarMovimientos), Permisos.RegistrarMovimientos)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTareas), Permisos.GestionarTareas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarDocumentos), Permisos.GestionarDocumentos)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerFinanzas), Permisos.VerFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarMaestrosFinanzas), Permisos.GestionarMaestrosFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTablasMaestras), Permisos.GestionarTablasMaestras)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerReportes), Permisos.VerReportes)]
    [InlineData(nameof(ShellMainViewModel.PuedeIngresarPorFactura), Permisos.RegistrarMovimientos)]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarEntradaSalida), Permisos.RegistrarMovimientos)]
    public void Admin_TodasLasPropiedadesPuede_SonTrue(string propiedad, string permisoIgnorado)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        var valor = (bool)typeof(ShellMainViewModel).GetProperty(propiedad)!.GetValue(vm)!;

        Assert.True(valor);
    }

    [Theory]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarProductos), Permisos.GestionarProductos)]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarMovimientos), Permisos.RegistrarMovimientos)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTareas), Permisos.GestionarTareas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarDocumentos), Permisos.GestionarDocumentos)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerFinanzas), Permisos.VerFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarMaestrosFinanzas), Permisos.GestionarMaestrosFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTablasMaestras), Permisos.GestionarTablasMaestras)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerReportes), Permisos.VerReportes)]
    public void Operador_ConElPermisoEnPermisosActuales_LaPropiedadEsTrue(string propiedad, string permiso)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { permiso });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        var valor = (bool)typeof(ShellMainViewModel).GetProperty(propiedad)!.GetValue(vm)!;

        Assert.True(valor);
    }

    [Theory]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarProductos))]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarMovimientos))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTareas))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarDocumentos))]
    [InlineData(nameof(ShellMainViewModel.PuedeVerFinanzas))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarMaestrosFinanzas))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTablasMaestras))]
    [InlineData(nameof(ShellMainViewModel.PuedeVerReportes))]
    [InlineData(nameof(ShellMainViewModel.PuedeIngresarPorFactura))]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarEntradaSalida))]
    public void Operador_SinNingunPermisoEnPermisosActuales_LaPropiedadEsFalse(string propiedad)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        var valor = (bool)typeof(ShellMainViewModel).GetProperty(propiedad)!.GetValue(vm)!;

        Assert.False(valor);
    }

    // ── PuedeIngresarPorFactura (fix bug de coherencia de permisos, 2026-08-15) ────────────────
    // A diferencia de las demás Puede*, esta combina CUATRO permisos porque el flujo real los
    // exige los cuatro: RegistrarMovimientos y RegistrarGastos (ambos verificados sin condición
    // por IngresoPorFacturaService.RegistrarAsync/AnularLoteAsync), VerFinanzas (sin él, los
    // combos de fuente/rubro/línea POA de la pantalla quedan vacíos — FuenteFinanciamientoService
    // y RubroGastoService.ListarActivas/os exigen VerFinanzas — y GuardarCommand queda
    // permanentemente deshabilitado porque PuedeGuardar exige FuenteSeleccionada y
    // RubroSeleccionado no nulos) y GestionarProductos (auditoría 2026-08-16: a diferencia de
    // /proveedores/activas → VerFinanzas, los endpoints GET /productos, /categorias/activas y
    // /unidades-medida/activas NO tienen ruta alternativa de lectura más laxa — exigen
    // GestionarProductos sin excepción. IngresoPorFacturaViewModel.InicializarAsync los usa para
    // poblar ProductosDisponibles, el ÚNICO combo para elegir un producto EXISTENTE en un
    // renglón — no es exclusivo del alta de producto nuevo. Sin GestionarProductos ese combo
    // queda vacío y la pantalla es inusable para el caso base, no solo para alta/actualización
    // de precio de costo).

    [Fact]
    public void Operador_ConSoloRegistrarMovimientos_PuedeIngresarPorFactura_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarMovimientos });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeIngresarPorFactura);
    }

    [Fact]
    public void Operador_ConRegistrarMovimientosYRegistrarGastosSinVerFinanzas_PuedeIngresarPorFactura_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>
        {
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos,
        });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeIngresarPorFactura);
    }

    [Fact]
    public void Operador_ConLosTresPermisosOriginalesSinGestionarProductos_PuedeIngresarPorFactura_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>
        {
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos, Permisos.VerFinanzas,
        });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeIngresarPorFactura);
    }

    [Fact]
    public void Operador_ConLosCuatroPermisosCompletos_PuedeIngresarPorFactura_EsTrue()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>
        {
            Permisos.RegistrarMovimientos, Permisos.RegistrarGastos, Permisos.VerFinanzas,
            Permisos.GestionarProductos,
        });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.True(vm.PuedeIngresarPorFactura);
    }

    [Fact]
    public async Task Navegacion_RefrescaPermisos_NotificaPuedeIngresarPorFactura()
    {
        var permisos = new HashSet<string>();
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(() => permisos);

        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync(() =>
        {
            permisos.Add(Permisos.RegistrarMovimientos);
            permisos.Add(Permisos.RegistrarGastos);
            permisos.Add(Permisos.VerFinanzas);
            permisos.Add(Permisos.GestionarProductos);
            return (IReadOnlySet<string>)permisos;
        });

        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), authMock.Object);

        Assert.False(vm.PuedeIngresarPorFactura);

        var propiedadesNotificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => propiedadesNotificadas.Add(e.PropertyName);

        navMock.Raise(n => n.Cambiado += null);
        await vm._tareaRefrescoPermisos;

        Assert.Contains(nameof(ShellMainViewModel.PuedeIngresarPorFactura), propiedadesNotificadas);
        Assert.True(vm.PuedeIngresarPorFactura);
    }

    // ── PuedeRegistrarEntradaSalida (fix bug de coherencia de permisos, 2026-08-16) ────────────
    // Entrada/Salida de movimientos combinan DOS permisos porque MovimientoRegistroViewModelBase.
    // InicializarAsync carga ProductoService.BuscarAsync (GET /productos, ProductosEndpoints.cs),
    // que exige GestionarProductos sin ruta alternativa — mismo caso que ProductosDisponibles en
    // IngresoPorFacturaViewModel (PuedeIngresarPorFactura). El combo de MovimientoFormControl es
    // el ÚNICO modo de elegir un producto existente (no hay campo de código/SKU/escaneo), así que
    // sin GestionarProductos la pantalla es inusable en el caso base, no solo para gestión de
    // catálogo.

    [Fact]
    public void Operador_ConSoloRegistrarMovimientos_PuedeRegistrarEntradaSalida_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarMovimientos });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeRegistrarEntradaSalida);
    }

    [Fact]
    public void Operador_ConSoloGestionarProductos_PuedeRegistrarEntradaSalida_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.GestionarProductos });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeRegistrarEntradaSalida);
    }

    [Fact]
    public void Operador_ConRegistrarMovimientosYGestionarProductos_PuedeRegistrarEntradaSalida_EsTrue()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>
        {
            Permisos.RegistrarMovimientos, Permisos.GestionarProductos,
        });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.True(vm.PuedeRegistrarEntradaSalida);
    }

    [Fact]
    public async Task Navegacion_RefrescaPermisos_NotificaPuedeRegistrarEntradaSalida()
    {
        var permisos = new HashSet<string>();
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(() => permisos);

        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync(() =>
        {
            permisos.Add(Permisos.RegistrarMovimientos);
            permisos.Add(Permisos.GestionarProductos);
            return (IReadOnlySet<string>)permisos;
        });

        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), authMock.Object);

        Assert.False(vm.PuedeRegistrarEntradaSalida);

        var propiedadesNotificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => propiedadesNotificadas.Add(e.PropertyName);

        navMock.Raise(n => n.Cambiado += null);
        await vm._tareaRefrescoPermisos;

        Assert.Contains(nameof(ShellMainViewModel.PuedeRegistrarEntradaSalida), propiedadesNotificadas);
        Assert.True(vm.PuedeRegistrarEntradaSalida);
    }

    // ── PuedeVerHistorialPorProducto (fix bug de coherencia de permisos, 2026-08-16) ───────────
    // Auditoría: ReporteStockService.ObtenerHistorialPorProductoAsync verifica VerReportes, pero
    // DELEGA en MovimientoStockService.ObtenerHistorialAsync, que exige RegistrarMovimientos --
    // un permiso independiente. El comentario "DOBLE-GUARD" original asumía que VerReportes era
    // Admin-only, premisa que dejó de ser cierta cuando pasó a ser configurable por usuario. En
    // vez de relajar el servicio delegado, se endurece el gate de ENTRADA de la pantalla: el
    // gate exige el MÁXIMO de los permisos de las capas de abajo, nunca el mínimo. Un Operador
    // con VerReportes pero sin RegistrarMovimientos ya NO ve "Historial por producto" en el
    // sidebar (antes lo veía y cada búsqueda le tiraba 403).

    [Fact]
    public void PuedeVerHistorialPorProducto_Admin_EsTrue()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.True(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void Operador_ConSoloVerReportes_PuedeVerHistorialPorProducto_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.VerReportes });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void Operador_ConSoloRegistrarMovimientos_PuedeVerHistorialPorProducto_EsFalse()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarMovimientos });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.False(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void Operador_ConVerReportesYRegistrarMovimientos_PuedeVerHistorialPorProducto_EsTrue()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>
        {
            Permisos.VerReportes, Permisos.RegistrarMovimientos,
        });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>());

        Assert.True(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public async Task Navegacion_RefrescaPermisos_NotificaPuedeVerHistorialPorProducto()
    {
        var permisos = new HashSet<string>();
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(() => permisos);

        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync(() =>
        {
            permisos.Add(Permisos.VerReportes);
            permisos.Add(Permisos.RegistrarMovimientos);
            return (IReadOnlySet<string>)permisos;
        });

        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), authMock.Object);

        Assert.False(vm.PuedeVerHistorialPorProducto);

        var propiedadesNotificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => propiedadesNotificadas.Add(e.PropertyName);

        navMock.Raise(n => n.Cambiado += null);
        await vm._tareaRefrescoPermisos;

        Assert.Contains(nameof(ShellMainViewModel.PuedeVerHistorialPorProducto), propiedadesNotificadas);
        Assert.True(vm.PuedeVerHistorialPorProducto);
    }
}
