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
using StockApp.Presentation.ViewModels.Reportes;
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
        // Grupos/ItemNavegacion (Task 5.2) evalúan las Puede* en el constructor para armar el
        // sidebar; sin este setup, un Operador (RolActual != Admin) dispara
        // PermisosActuales.Contains(...) sobre un mock sin configurar -> NullReferenceException.
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"), confirmMock.Object,
            Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
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
            Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
    public void NavCatalogos_NavegaACatalogosYMarcaLaSeccion()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavCatalogosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<StockApp.Presentation.ViewModels.Administracion.CatalogosViewModel>(), Times.Once);
        Assert.Equal("Catalogos", vm.SeccionActiva);
    }

    [Fact]
    public void Formulario_DeCatalogo_ConservaLaSeccionCatalogos()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);
        vm.NavCatalogosCommand.Execute(null);
        var formulario = new StockApp.Presentation.ViewModels.Catalogo.CategoriaFormViewModel(
            Mock.Of<StockApp.Application.Catalogo.ICategoriaService>(), navMock.Object);
        navMock.Setup(n => n.Actual).Returns(formulario);

        navMock.Raise(n => n.Cambiado += null);

        Assert.Same(formulario, vm.CurrentContent);
        Assert.Equal("Catalogos", vm.SeccionActiva);
    }

    [Fact]
    public void Volver_ACatalogosDesdeOtraSeccion_MarcaCatalogos()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);
        vm.NavProductosCommand.Execute(null);
        var nav = Mock.Of<INavigationService>();
        var conf = Mock.Of<StockApp.Presentation.Services.IConfirmacionService>();
        var sesion = Mock.Of<ICurrentSession>(x => x.RolActual == RolUsuario.Admin && x.PermisosActuales == new HashSet<string>());
        var catalogos = new StockApp.Presentation.ViewModels.Administracion.CatalogosViewModel(sesion,
            new StockApp.Presentation.ViewModels.Catalogo.CategoriaListViewModel(Mock.Of<StockApp.Application.Catalogo.ICategoriaService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Catalogo.ProveedorListViewModel(Mock.Of<StockApp.Application.Catalogo.IProveedorService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Catalogo.UnidadMedidaListViewModel(Mock.Of<StockApp.Application.Catalogo.IUnidadMedidaService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Finanzas.FuenteFinanciamientoListViewModel(Mock.Of<StockApp.Application.Finanzas.IFuenteFinanciamientoService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Finanzas.RubroGastoListViewModel(Mock.Of<StockApp.Application.Finanzas.IRubroGastoService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Finanzas.LineaPoaListViewModel(Mock.Of<StockApp.Application.Finanzas.ILineaPoaService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Catalogo.ZonaListViewModel(Mock.Of<StockApp.Application.Catalogo.IZonaService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Catalogo.DimensionTematicaListViewModel(Mock.Of<StockApp.Application.Catalogo.IDimensionTematicaService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Catalogo.OrganismoResponsableListViewModel(Mock.Of<StockApp.Application.Catalogo.IOrganismoResponsableService>(), nav, conf),
            new StockApp.Presentation.ViewModels.Catalogo.OrigenFinanciamientoListViewModel(Mock.Of<StockApp.Application.Catalogo.IOrigenFinanciamientoService>(), nav, conf));
        navMock.Setup(n => n.Actual).Returns(catalogos);

        navMock.Raise(n => n.Cambiado += null);

        Assert.Equal("Catalogos", vm.SeccionActiva);
    }

    [Fact]
    public void NotificarPermisosActualizados_AvisaQueCambioPuedeGestionarCatalogos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.NotificarPermisosActualizados();

        Assert.Contains(nameof(ShellMainViewModel.PuedeGestionarCatalogos), avisos);
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
    public void NavReportes_EstableceSeccionActiva_Reportes_YNavegaAlHub()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavReportesCommand.Execute(null);

        Assert.Equal("Reportes", vm.SeccionActiva);
        navMock.Verify(n => n.Navegar<ReportesViewModel>(), Times.Once);
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
    //
    // Importación quedó apagada por ShellMainViewModel.ImportacionHabilitada = false
    // (2026-09-04, pedido del usuario: el módulo no se va a usar). Los dos tests de abajo
    // documentaban el comportamiento ORIGINAL (navega y cambia de sección); ahora documentan
    // el no-op, uno por aserción (así el nombre del test que falla dice cuál de las dos cosas
    // se rompió). Si se reactiva la constante, revertir:
    // - NavImportacion_ConModuloDeshabilitado_NoLlamaNavegar: Times.Never -> Times.Once
    // - NavImportacion_ConModuloDeshabilitado_NoEstableceSeccionActiva:
    //   Assert.Equal(seccionOriginal, ...) -> Assert.Equal("Importacion", vm.SeccionActiva)

    [Fact]
    public void NavImportacion_ConModuloDeshabilitado_NoLlamaNavegar()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        navMock.Verify(n => n.Navegar<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>(), Times.Never);
    }

    [Fact]
    public void NavImportacion_ConModuloDeshabilitado_NoEstableceSeccionActiva()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);
        var seccionOriginal = vm.SeccionActiva;

        vm.NavImportacionCommand.Execute(null);

        Assert.Equal(seccionOriginal, vm.SeccionActiva);
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
            Mock.Of<IConfirmacionService>(), authMock.Object, Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), authMock.Object, Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), authMock.Object, Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), authMock.Object, Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());

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
            Mock.Of<IConfirmacionService>(), authMock.Object, Mock.Of<IServicioPreferenciasSidebar>());

        Assert.False(vm.PuedeRegistrarEntradaSalida);

        var propiedadesNotificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => propiedadesNotificadas.Add(e.PropertyName);

        navMock.Raise(n => n.Cambiado += null);
        await vm._tareaRefrescoPermisos;

        Assert.Contains(nameof(ShellMainViewModel.PuedeRegistrarEntradaSalida), propiedadesNotificadas);
        Assert.True(vm.PuedeRegistrarEntradaSalida);
    }
}
