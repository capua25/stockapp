using Moq;
using StockApp.ApiClient;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Alertas;
using StockApp.Application.Auth;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Application.Logs;
using StockApp.Application.Tareas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ShellViewModelTests
{
    private static readonly IInfoApp InfoAppStub = Mock.Of<IInfoApp>(x => x.Version == "0.0.0");

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fake de IUiDispatcher: ejecuta inline (sin depender de Dispatcher.UIThread real,
    /// no inicializado en tests unitarios sin Application de Avalonia).
    /// </summary>
    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public void Post(Action accion) => accion();
    }

    /// <summary>
    /// Igual que <see cref="Crear"/> pero devuelve también el NavigationService compartido,
    /// para que los tests de desconexión (leak fix) puedan disparar Cambiado directamente y
    /// verificar que una instancia de ShellMainViewModel desconectada ya no reacciona.
    /// </summary>
    /// <remarks>
    /// IMPORTANTE: ShellMainViewModel debe recibir el MISMO INavigationService que usa
    /// ShellViewModel (navSvc), no un Mock.Of&lt;INavigationService&gt;() nuevo — así es como
    /// App.axaml.cs los cablea en producción (ambos reciben el singleton). Si cada
    /// ShellMainViewModel se suscribe a una instancia de navegación distinta y aislada, los
    /// tests de desconexión pasan sin importar si Desconectar() se llama o no, porque nunca
    /// comparten el delegate Cambiado que hay que desuscribir.
    /// </remarks>
    private static (ShellViewModel Shell, NavigationService NavSvc) CrearConNavegacionExpuesta()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        NavigationService? navSvcRef = null;
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(
                    sessionMock.Object, navSvcRef!, InfoAppStub, confirmMock.Object, Mock.Of<IAuthService>(),
                    Mock.Of<IServicioPreferenciasSidebar>());
            if (t == typeof(InicioViewModel))
                return new InicioViewModel(
                    sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IFinanzasVistasService>(),
                    Mock.Of<IBackupsService>(), Mock.Of<ITareaService>(), Mock.Of<IAuthService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });
        navSvcRef = navSvc;

        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());

        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(true, "MAQ")); // activada → va al login

        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(),
            licenciaMock.Object,
            Mock.Of<IResetAdminService>(),
            navSvc,
            coordinador,
            new FakeUiDispatcher(),
            InfoAppStub);

        return (shell, navSvc);
    }

    private static ShellViewModel Crear() => CrearConNavegacionExpuesta().Shell;

    /// <summary>
    /// Helper para el modo acceso limitado (FIX 1, re-review final E1): configura ShellViewModel
    /// con un mantenimientoFactory funcional y licencia vencida. El NavigationService lanza si se
    /// invoca — el modo acotado no debe tocar INavigationService en absoluto (es la barrera real
    /// que impide llegar a Productos/Finanzas/Reportes, ver AccesoLimitadoViewModel).
    /// </summary>
    private static (ShellViewModel Shell, MantenimientoViewModel Mantenimiento) CrearConAccesoLimitado(
        IAuthService authService)
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<CorridaBackupDto>());

        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(false, "MAQ-1")); // vencida

        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());

        var navSvc = new NavigationService(
            t => throw new InvalidOperationException(
                $"El modo acceso limitado no debería navegar via INavigationService: {t.Name}"));

        var logsMock = new Mock<ILogsService>();
        logsMock.Setup(l => l.ObtenerResumenAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResumenLogsDto(0, null, null, 0));

        var mantenimiento = new MantenimientoViewModel(
            backupsMock.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(), logsMock.Object,
            Mock.Of<IConfiguracionAlertasService>());

        var shell = new ShellViewModel(
            authService,
            licenciaMock.Object,
            Mock.Of<IResetAdminService>(),
            navSvc,
            coordinador,
            new FakeUiDispatcher(),
            InfoAppStub,
            () => mantenimiento);

        return (shell, mantenimiento);
    }

    // ── tests: navegación de arranque ────────────────────────────────────────

    [Fact]
    public async Task Inicializar_MuestraLogin()
    {
        var shell = Crear();

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    // ── tests: navegación manual ─────────────────────────────────────────────

    [Fact]
    public void MostrarLogin_EstableceLoginViewModel()
    {
        var shell = Crear();

        shell.MostrarLogin();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarContenidoPrincipal_EstableceShellMainViewModel()
    {
        var shell = Crear();

        shell.MostrarContenidoPrincipal();

        Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarLoginConAviso_EstableceLoginConElMensaje()
    {
        var shell = Crear();

        shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo.");

        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.Equal("Sesión vencida, ingresá de nuevo.", login.MensajeError);
    }

    // ── tests: licencia (Inc 7 Fase B) ───────────────────────────────────────

    [Fact]
    public async Task Inicializar_LicenciaNoActivada_MuestraBloqueo()
    {
        // helper inline con licencia NO activada
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(false, "MAQ-1"));
        var navSvc = new NavigationService(_ => throw new InvalidOperationException());
        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());
        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(), licenciaMock.Object, Mock.Of<IResetAdminService>(),
            navSvc, coordinador, new FakeUiDispatcher(), InfoAppStub);

        await shell.InicializarAsync();

        Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarBloqueoLicencia_EstableceBloqueoLicenciaViewModel()
    {
        var shell = Crear();

        shell.MostrarBloqueoLicencia();

        Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Inicializar_ApiCaidaAlConsultarLicencia_MuestraLogin()
    {
        // API inalcanzable al arrancar: no debe tumbar el arranque, cae a login
        // (el login es quien muestra el error de conexión al usuario).
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ThrowsAsync(new ServidorNoDisponibleException());
        var navSvc = new NavigationService(_ => throw new InvalidOperationException());
        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());
        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(), licenciaMock.Object, Mock.Of<IResetAdminService>(),
            navSvc, coordinador, new FakeUiDispatcher(), InfoAppStub);

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarReset_EstableceResetAdminViewModel()
    {
        var shell = Crear();

        shell.MostrarReset();

        Assert.IsType<ResetAdminViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarBloqueoLicencia_LlamadoDosVeces_NoRecreaElViewModel()
    {
        // Idempotencia: varios 423 concurrentes (ver ApiSession.LicenciaDesactivada) no
        // deben re-navegar ni perder lo que el usuario ya pegó en el VM de bloqueo activo.
        var shell = Crear();

        shell.MostrarBloqueoLicencia();
        var primerVm = shell.CurrentViewModel;
        shell.MostrarBloqueoLicencia();

        Assert.Same(primerVm, shell.CurrentViewModel);
    }

    // ── fix leak de suscripción: ShellMainViewModel.Desconectar() ───────────────

    [Fact]
    public async Task Login_Logout_Login_Logout_NoDuplicaMostrarLogin()
    {
        // Cada ciclo login→logout crea una instancia nueva de ShellMainViewModel (Transient).
        // Este test cubre tres cosas a la vez: (1) que ShellViewModel desconecta la instancia
        // vieja antes de volver al login (fix del leak sobre INavigationService.Cambiado);
        // (2) el chequeo simétrico que pidió el reviewer: como ShellMainViewModel.
        // CerrarSesionSolicitado se suscribe una sola vez por instancia nueva, un segundo
        // ciclo no debe duplicar el MostrarLogin (un solo handler disparando por evento); y
        // (3) la retención real: shellMain1 no debe reaccionar a Cambiado del NavigationService
        // compartido después de haber sido desconectado (ver comentario más abajo).
        var (shell, navSvc) = CrearConNavegacionExpuesta();

        var vecesQueMostroLogin = 0;
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentViewModel) &&
                shell.CurrentViewModel is LoginViewModel)
                vecesQueMostroLogin++;
        };

        shell.MostrarContenidoPrincipal();
        var shellMain1 = Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
        await shellMain1.CerrarSesionCommand.ExecuteAsync(null);

        Assert.Equal(1, vecesQueMostroLogin);
        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);

        var contenidoShellMain1TrasLogout = shellMain1.CurrentContent;

        shell.MostrarContenidoPrincipal();
        var shellMain2 = Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
        Assert.NotSame(shellMain1, shellMain2);

        // Verificación real de la retención (no solo del conteo de MostrarLogin): el segundo
        // MostrarContenidoPrincipal() navega sobre navSvc (el MISMO NavigationService que
        // ShellMainViewModel.OnNavegacionCambiada escucha en producción), lo que dispara
        // Cambiado con Actual = shellMain2. Si shellMain1 no hubiese sido desconectado en el
        // logout, su handler viejo seguiría subscripto al singleton y este assert fallaría:
        // shellMain1.CurrentContent pasaría a ser shellMain2 (ReferenceEquals(Actual, this) es
        // false para shellMain1, así que su guard no lo protege). Con el fix, shellMain1 quedó
        // desconectado y su CurrentContent no cambia.
        Assert.Same(contenidoShellMain1TrasLogout, shellMain1.CurrentContent);

        await shellMain2.CerrarSesionCommand.ExecuteAsync(null);

        Assert.Equal(2, vecesQueMostroLogin);
    }

    [Fact]
    public void MostrarLoginConAviso_ConShellMainActivo_DesconectaLaInstanciaVieja()
    {
        // Camino de salida #2 (sesión vencida / 401, ver ApiSession.SesionVencida cableado en
        // App.axaml.cs). Antes del fix, este camino no llamaba a Desconectar() y dejaba el
        // ShellMainViewModel activo retenido para siempre por el Singleton INavigationService.
        var (shell, navSvc) = CrearConNavegacionExpuesta();

        shell.MostrarContenidoPrincipal();
        var shellMainActivo = Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
        var contenidoAntes = shellMainActivo.CurrentContent;

        shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo.");

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);

        // Disparamos otra navegación sobre el mismo NavigationService compartido: si
        // shellMainActivo no hubiese sido desconectado, su handler viejo reaccionaría y
        // actualizaría CurrentContent.
        navSvc.Navegar<InicioViewModel>();

        Assert.Same(contenidoAntes, shellMainActivo.CurrentContent);
    }

    [Fact]
    public void MostrarBloqueoLicencia_ConShellMainActivo_DesconectaLaInstanciaVieja()
    {
        // Camino de salida #3 (423 a mitad de sesión, ver comentario de MostrarBloqueoLicencia).
        // Antes del fix, este camino tampoco llamaba a Desconectar().
        var (shell, navSvc) = CrearConNavegacionExpuesta();

        shell.MostrarContenidoPrincipal();
        var shellMainActivo = Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
        var contenidoAntes = shellMainActivo.CurrentContent;

        shell.MostrarBloqueoLicencia();

        Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);

        navSvc.Navegar<InicioViewModel>();

        Assert.Same(contenidoAntes, shellMainActivo.CurrentContent);
    }

    // ── tests: FIX 1 (IMPORTANT, re-review final E1) — modo acceso limitado ─────

    [Fact]
    public async Task FlujoAccesoLimitado_BloqueoLoginYLoginExitoso_LlegaAMantenimiento()
    {
        // Camino completo del fix: antes, BloqueoLicenciaViewModel era un callejón sin salida
        // (solo activar licencia, sin login) — con licencia vencida no había forma de llegar
        // a los backups desde la app, aunque /auth/login y /backups ya estuvieran exentos del
        // bloqueo del lado servidor.
        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.LoginAsync("admin", "secreto")).ReturnsAsync(LoginResult.Ok());
        var (shell, mantenimiento) = CrearConAccesoLimitado(authMock.Object);

        shell.MostrarBloqueoLicencia();
        var bloqueo = Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);

        bloqueo.IrALoginAccesoLimitadoCommand.Execute(null);
        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.True(login.SoloAccesoLimitado);

        login.NombreUsuario = "admin";
        login.Contrasena    = "secreto";
        await login.EntrarCommand.ExecuteAsync(null);

        var acceso = Assert.IsType<AccesoLimitadoViewModel>(shell.CurrentViewModel);
        Assert.Same(mantenimiento, acceso.Mantenimiento);
    }

    [Fact]
    public async Task MostrarBloqueoLicencia_EnModoAccesoLimitado_NoSacaAlUsuario()
    {
        // Un 423 durante la descarga en modo acotado es esperable (la licencia sigue inactiva
        // a propósito) y no debe patear al admin del único camino que tiene a los backups —
        // ver ApiSession.LicenciaDesactivada, cableado en App.axaml.cs a MostrarBloqueoLicencia.
        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(LoginResult.Ok());
        var (shell, _) = CrearConAccesoLimitado(authMock.Object);

        shell.MostrarBloqueoLicencia();
        ((BloqueoLicenciaViewModel)shell.CurrentViewModel!).IrALoginAccesoLimitadoCommand.Execute(null);
        var login = (LoginViewModel)shell.CurrentViewModel!;
        login.NombreUsuario = "admin";
        login.Contrasena    = "secreto";
        await login.EntrarCommand.ExecuteAsync(null);
        var accesoLimitado = Assert.IsType<AccesoLimitadoViewModel>(shell.CurrentViewModel);

        // Simula el handler de 423.
        shell.MostrarBloqueoLicencia();

        Assert.Same(accesoLimitado, shell.CurrentViewModel);
    }

    [Fact]
    public async Task MostrarLoginConAviso_EnModoAccesoLimitado_ElReloginSigueAcotado()
    {
        // Si el token expira (401) a mitad de la sesión acotada (ApiSession.SesionVencida →
        // MostrarLoginConAviso, cableado en App.axaml.cs), el re-login tiene que seguir siendo
        // acotado: sin esto, un segundo login exitoso navegaría al shell completo pese a que
        // la licencia sigue vencida — el bypass que el modo acotado existe para evitar.
        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(LoginResult.Ok());
        var (shell, _) = CrearConAccesoLimitado(authMock.Object);

        shell.MostrarBloqueoLicencia();
        ((BloqueoLicenciaViewModel)shell.CurrentViewModel!).IrALoginAccesoLimitadoCommand.Execute(null);
        var login1 = (LoginViewModel)shell.CurrentViewModel!;
        login1.NombreUsuario = "admin";
        login1.Contrasena    = "secreto";
        await login1.EntrarCommand.ExecuteAsync(null);
        Assert.IsType<AccesoLimitadoViewModel>(shell.CurrentViewModel);

        shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo.");

        var login2 = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.True(login2.SoloAccesoLimitado);

        login2.NombreUsuario = "admin";
        login2.Contrasena    = "secreto";
        await login2.EntrarCommand.ExecuteAsync(null);

        Assert.IsType<AccesoLimitadoViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarAccesoLimitado_SinFactoryConfigurada_Lanza()
    {
        // Guard defensivo: los tests que construyen ShellViewModel sin el 8vo parámetro
        // (mantenimientoFactory = null) no deberían poder entrar en modo acotado en silencio.
        var shell = Crear();

        Assert.Throws<InvalidOperationException>(() => shell.MostrarAccesoLimitado());
    }

    [Fact]
    public async Task MostrarReset_EnModoAccesoLimitado_VolverSigueAcotado()
    {
        // Fix (MINOR, tercer review final E1): antes, reset.Volver llamaba a MostrarLogin()
        // pelado sin importar de dónde venía el reset. Secuencia real: bloqueo por licencia →
        // login acotado → "No puedo entrar" → reset admin → Volver → login SIN el flag → login
        // exitoso → shell completo con la licencia vencida (que después devuelve 423 en todas
        // las pantallas). Mismo criterio que MostrarLoginConAviso_EnModoAccesoLimitado_
        // ElReloginSigueAcotado ya cubre para el 401.
        var authMock = new Mock<IAuthService>();
        authMock.Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(LoginResult.Ok());
        var (shell, _) = CrearConAccesoLimitado(authMock.Object);

        shell.MostrarBloqueoLicencia();
        ((BloqueoLicenciaViewModel)shell.CurrentViewModel!).IrALoginAccesoLimitadoCommand.Execute(null);
        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.True(login.SoloAccesoLimitado);

        login.ResetearAdminCommand.Execute(null);
        var reset = Assert.IsType<ResetAdminViewModel>(shell.CurrentViewModel);

        reset.VolverCommand.Execute(null);

        var loginTrasVolver = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.True(loginTrasVolver.SoloAccesoLimitado);

        loginTrasVolver.NombreUsuario = "admin";
        loginTrasVolver.Contrasena = "secreto";
        await loginTrasVolver.EntrarCommand.ExecuteAsync(null);

        Assert.IsType<AccesoLimitadoViewModel>(shell.CurrentViewModel);
    }
}
