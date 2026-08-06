using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class InicioViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// tareas/errorTareas se agregan sin tocar la forma de la tupla que devuelve este helper
    /// (20+ tests existentes la desestructuran con discards posicionales) -- Mock&lt;ITareaService&gt;
    /// queda solo local, armado con ReturnsAsync/ThrowsAsync según lo que pida cada test nuevo.
    /// </summary>
    private static (InicioViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock,
                     Mock<IFinanzasVistasService> finanzasMock, Mock<IBackupsService> backupsMock)
        Crear(
            UsuarioSesion usuario, CalendarioPagosDto? calendario = null, SaludBackupDto? salud = null,
            IReadOnlyList<Tarea>? tareas = null, Exception? errorTareas = null)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.UsuarioActual).Returns(usuario);
        sessionMock.Setup(s => s.RolActual).Returns(usuario.Rol);

        var navMock = new Mock<INavigationService>();
        var finanzasMock = new Mock<IFinanzasVistasService>();
        finanzasMock.Setup(f => f.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            calendario ?? new CalendarioPagosDto(
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ObtenerSaludAsync()).ReturnsAsync(salud ?? new SaludBackupDto(DateTime.UtcNow, false, 26));

        var tareasMock = new Mock<ITareaService>();
        if (errorTareas is not null)
            tareasMock.Setup(t => t.ListarAsync()).ThrowsAsync(errorTareas);
        else
            tareasMock.Setup(t => t.ListarAsync()).ReturnsAsync(tareas ?? new List<Tarea>());

        var vm = new InicioViewModel(
            sessionMock.Object, navMock.Object, finanzasMock.Object, backupsMock.Object, tareasMock.Object);
        return (vm, sessionMock, navMock, finanzasMock, backupsMock);
    }

    // ── Saludo ───────────────────────────────────────────────────────────────

    [Fact]
    public void Saludo_IncluyeNombreCompleto_CuandoEstaPresente()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario);

        Assert.Contains("Juan Pérez", vm.Saludo);
    }

    [Fact]
    public void Saludo_CaeANombreUsuario_CuandoNombreCompletoEsNull()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, null);
        var (vm, _, _, _, _) = Crear(usuario);

        Assert.Contains("jperez", vm.Saludo);
    }

    // ── EsAdmin / RolTexto ───────────────────────────────────────────────────

    [Fact]
    public void EsAdmin_True_ConRolAdmin()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario);

        Assert.True(vm.EsAdmin);
    }

    [Fact]
    public void EsAdmin_False_ConRolOperador()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario);

        Assert.False(vm.EsAdmin);
    }

    [Fact]
    public void RolTexto_Administrador_ConRolAdmin()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario);

        Assert.Equal("Administrador", vm.RolTexto);
    }

    [Fact]
    public void RolTexto_Operador_ConRolOperador()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario);

        Assert.Equal("Operador", vm.RolTexto);
    }

    // ── Comandos de acceso rápido ────────────────────────────────────────────

    [Fact]
    public void IrAProductos_LlamaNavegar_AProductoListViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.IrAProductosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void IrARegistrarEntrada_LlamaNavegar_AEntradaRegistroViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.IrARegistrarEntradaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<EntradaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void IrARegistrarSalida_LlamaNavegar_ASalidaRegistroViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.IrARegistrarSalidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<SalidaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void IrAHistorialMovimientos_LlamaNavegar_AMovimientoHistorialViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.IrAHistorialMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public void IrAValorizacion_LlamaNavegar_AValorizacionViewModel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.IrAValorizacionCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ValorizacionViewModel>(), Times.Once);
    }

    [Fact]
    public void IrAAuditoria_LlamaNavegar_AAuditoriaLogViewModel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.IrAAuditoriaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<AuditoriaLogViewModel>(), Times.Once);
    }

    // ── Aviso de vencimientos (nuevo) ───────────────────────────────────────

    [Fact]
    public async Task CargarAsync_ConFacturasVencidas_MuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoVencimientos);
        Assert.Equal(1, vm.CantidadVencidas);
        Assert.Equal(0, vm.CantidadAVencer7Dias);
    }

    [Fact]
    public async Task CargarAsync_ConAVencerEn7DiasSinVencidas_MuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto>(),
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 20), "PorVencer") },
            new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoVencimientos);
        Assert.Equal(0, vm.CantidadVencidas);
        Assert.Equal(1, vm.CantidadAVencer7Dias);
    }

    [Fact]
    public async Task CargarAsync_SinVencidasNiAVencer_NoMuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario);

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task CargarAsync_ElServicioFalla_NoRompeYOcultaElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, finanzas, _) = Crear(usuario);
        finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(null))
            .ThrowsAsync(new UnauthorizedAccessException());

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoVencimientos);
    }

    // ── Aviso de salud de backup (nuevo) ─────────────────────────────────────

    [Fact]
    public async Task CargarAsync_AdminConBackupVencido_MuestraAvisoBackup()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario, salud: new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoBackup);
        Assert.True(vm.MostrarAvisoBackupProblema);
        Assert.False(vm.MostrarAvisoBackupDesconocido);
        Assert.NotNull(vm.TextoAvisoBackup);
        Assert.Contains("26", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task CargarAsync_AdminConBackupVencido_UsaHoraLocalSinEtiquetaUtc()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var ultimoExitoUtc = new DateTime(2026, 7, 20, 3, 0, 0, DateTimeKind.Utc);
        var (vm, _, _, _, _) = Crear(usuario, salud: new SaludBackupDto(ultimoExitoUtc, true, 26));

        await vm.CargarAsync();

        // Mismo criterio que el converter FechaUtcALocalConverter (MantenimientoView): la fecha
        // persistida en UTC se muestra en hora LOCAL de la máquina, no cruda con etiqueta "UTC"
        // — antes el mismo backup aparecía con distinta hora según la pantalla.
        var esperadoLocal = DateTime.SpecifyKind(ultimoExitoUtc, DateTimeKind.Utc).ToLocalTime();
        Assert.Contains(esperadoLocal.ToString("dd/MM/yyyy HH:mm"), vm.TextoAvisoBackup);
        Assert.DoesNotContain("UTC", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task CargarAsync_AdminConBackupAlDia_NoMuestraAvisoBackup()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario, salud: new SaludBackupDto(DateTime.UtcNow, false, 26));

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoBackup);
        Assert.False(vm.MostrarAvisoBackupProblema);
        Assert.False(vm.MostrarAvisoBackupDesconocido);
    }

    [Fact]
    public async Task CargarAsync_AdminSinBackupsExitosos_MuestraTextoDeInstalacionNueva()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario, salud: new SaludBackupDto(null, true, 26));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoBackup);
        Assert.Equal("Todavía no se registró ningún backup exitoso.", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task CargarAsync_Operador_NuncaConsultaSaludDeBackup()
    {
        var usuario = new UsuarioSesion(2, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, backupsMock) = Crear(usuario);

        await vm.CargarAsync();

        backupsMock.Verify(b => b.ObtenerSaludAsync(), Times.Never);
        Assert.False(vm.MostrarAvisoBackup);
        Assert.False(vm.MostrarAvisoBackupDesconocido);
    }

    /// <summary>
    /// Antes este test se llamaba "...NoRompeYOcultaElAviso" y afirmaba
    /// Assert.False(vm.MostrarAvisoBackup) — congelaba el bug real del review: un fallo
    /// consultando /backups/salud (API caída, 403, 404 por versión vieja, cambio de forma del
    /// JSON) se renderizaba IGUAL que "backup al día", maquillando en silencio la falta total
    /// de información. Decisión del usuario: tercer estado explícito ("no se pudo verificar"),
    /// ni ocultar el aviso ni afirmar que el backup falló. Lo que SIGUE valiendo (y este test
    /// también lo cubre) es que un fallo acá nunca debe romper CargarAsync ni tocar el aviso de
    /// vencimientos, que se resuelve en un try/catch totalmente independiente.
    /// </summary>
    [Fact]
    public async Task CargarAsync_ServicioDeBackupFalla_MuestraAvisoDeEstadoDesconocidoSinRomper()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.UsuarioActual).Returns(usuario);
        sessionMock.Setup(s => s.RolActual).Returns(usuario.Rol);
        var navMock = new Mock<INavigationService>();
        var finanzasMock = new Mock<IFinanzasVistasService>();
        finanzasMock.Setup(f => f.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            new CalendarioPagosDto(
                new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ObtenerSaludAsync()).ThrowsAsync(new InvalidOperationException("servidor caído"));

        var tareasMock = new Mock<ITareaService>();
        tareasMock.Setup(t => t.ListarAsync()).ReturnsAsync(new List<Tarea>());

        var vm = new InicioViewModel(
            sessionMock.Object, navMock.Object, finanzasMock.Object, backupsMock.Object, tareasMock.Object);

        await vm.CargarAsync();

        // Tercer estado: se avisa (no se oculta), pero como "desconocido", no como "problema".
        Assert.True(vm.MostrarAvisoBackup);
        Assert.True(vm.MostrarAvisoBackupDesconocido);
        Assert.False(vm.MostrarAvisoBackupProblema);
        Assert.Equal("No se pudo verificar el estado del backup.", vm.TextoAvisoBackup);

        // Lo que seguía valiendo antes de este fix: el fallo de backup no afecta el aviso de
        // vencimientos, que se resuelve en su propio try/catch, aguas arriba en CargarAsync.
        Assert.True(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task IrACalendarioPagos_NavegaACalendarioPagosViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, nav, _, _) = Crear(usuario);

        vm.IrACalendarioPagosCommand.Execute(null);

        nav.Verify(n => n.Navegar<CalendarioPagosViewModel>(), Times.Once);
    }

    [Fact]
    public async Task TextoVencidas_Singular_ConUnaFacturaVencida()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.Equal("1 factura vencida", vm.TextoVencidas);
    }

    [Fact]
    public async Task TextoVencidas_Plural_ConVariasFacturasVencidas()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto>
            {
                new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida"),
                new(2, "Barraca Y", "A-2", 300m, new DateOnly(2026, 7, 2), "Vencida"),
            },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.Equal("2 facturas vencidas", vm.TextoVencidas);
    }

    [Fact]
    public async Task TextoAVencer7Dias_Singular_ConUnaFacturaPorVencer()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto>(),
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 20), "PorVencer") },
            new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.Equal("1 factura por vencer esta semana", vm.TextoAVencer7Dias);
    }

    [Fact]
    public async Task TextoAVencer7Dias_Plural_ConVariasFacturasPorVencer()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto>(),
            new List<FacturaCalendarioDto>
            {
                new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 20), "PorVencer"),
                new(2, "Barraca Y", "A-2", 300m, new DateOnly(2026, 7, 21), "PorVencer"),
                new(3, "Barraca Z", "A-3", 200m, new DateOnly(2026, 7, 22), "PorVencer"),
            },
            new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.Equal("3 facturas por vencer esta semana", vm.TextoAVencer7Dias);
    }

    // ── Panel de vencimientos de tareas (nuevo, 2026-08-06) ──────────────────

    private static Tarea TareaCon(
        int id, string titulo, DateTime? fechaLimite, EstadoTarea estado = EstadoTarea.Pendiente,
        int? tomadaPorUsuarioId = null) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
        TomadaPorUsuarioId = tomadaPorUsuarioId,
    };

    [Fact]
    public async Task CargarAsync_ConTareasVencidasYProximas_LasExponeYMuestraElPanel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "Vencida", DateTime.UtcNow.Date.AddDays(-3)),
            TareaCon(2, "Por vencer", DateTime.UtcNow.Date.AddDays(2)),
        };
        var (vm, _, _, _, _) = Crear(usuario, tareas: tareas);

        await vm.CargarAsync();

        Assert.True(vm.MostrarPanelTareas);
        Assert.Single(vm.TareasVencidas);
        Assert.Single(vm.TareasProximasAVencer);
        Assert.Equal("Vencida", vm.TareasVencidas[0].Titulo);
        Assert.Equal("Por vencer", vm.TareasProximasAVencer[0].Titulo);
    }

    [Fact]
    public async Task CargarAsync_SinTareasQueRequieranAtencion_NoMuestraElPanel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea> { TareaCon(1, "Lejana", DateTime.UtcNow.Date.AddDays(30)) };
        var (vm, _, _, _, _) = Crear(usuario, tareas: tareas);

        await vm.CargarAsync();

        Assert.False(vm.MostrarPanelTareas);
        Assert.Empty(vm.TareasVencidas);
        Assert.Empty(vm.TareasProximasAVencer);
    }

    [Fact]
    public async Task CargarAsync_SinTareaAlguna_NoMuestraElPanel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, _) = Crear(usuario);

        await vm.CargarAsync();

        Assert.False(vm.MostrarPanelTareas);
    }

    /// <summary>
    /// La pantalla de Inicio nunca debe romperse por un fallo del servidor: si /tareas falla
    /// (API caída, sin permiso, etc.), el panel simplemente no se muestra -- degrada en
    /// silencio, igual que el resto de las zonas de esta pantalla (aviso de vencimientos,
    /// backup).
    /// </summary>
    [Fact]
    public async Task CargarAsync_ElServicioDeTareasFalla_NoRompeYOcultaElPanel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario, errorTareas: new InvalidOperationException("servidor caído"));

        await vm.CargarAsync();

        Assert.False(vm.MostrarPanelTareas);
        Assert.Empty(vm.TareasVencidas);
        Assert.Empty(vm.TareasProximasAVencer);
    }

    [Fact]
    public async Task CargarAsync_ElServicioDeTareasFalla_NoAfectaLosOtrosAvisosDeLaPantalla()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(
            usuario,
            calendario: new CalendarioPagosDto(
                new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()),
            errorTareas: new InvalidOperationException("servidor caído"));

        await vm.CargarAsync();

        Assert.False(vm.MostrarPanelTareas);
        Assert.True(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task CargarAsync_RolOperador_NoMuestraTareasTomadasPorOtroOperador()
    {
        var usuario = new UsuarioSesion(5, "jperez", RolUsuario.Operador, "Juan Pérez");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "De otro operador", DateTime.UtcNow.Date.AddDays(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 99),
            TareaCon(2, "Mía", DateTime.UtcNow.Date.AddDays(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 5),
            TareaCon(3, "De nadie", DateTime.UtcNow.Date, EstadoTarea.Pendiente),
        };
        var (vm, _, _, _, _) = Crear(usuario, tareas: tareas);

        await vm.CargarAsync();

        Assert.DoesNotContain(vm.TareasVencidas, f => f.Titulo == "De otro operador");
        Assert.Contains(vm.TareasVencidas, f => f.Titulo == "Mía");
        Assert.Contains(vm.TareasProximasAVencer, f => f.Titulo == "De nadie");
    }

    [Fact]
    public void VerTarea_NavegaATareaFormViewModel_ConLaTareaCorrecta()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, sessionMock, navMock, _, _) = Crear(usuario);
        var tarea = TareaCon(7, "Reponer stock depósito B", DateTime.UtcNow.Date.AddDays(-3));
        var fila = new TareaFila(tarea, RolUsuario.Admin);

        Action<TareaFormViewModel>? inicializador = null;
        navMock.Setup(n => n.Navegar(It.IsAny<Action<TareaFormViewModel>>()))
            .Callback<Action<TareaFormViewModel>>(a => inicializador = a);

        vm.VerTareaCommand.Execute(fila);

        navMock.Verify(n => n.Navegar(It.IsAny<Action<TareaFormViewModel>>()), Times.Once);
        Assert.NotNull(inicializador);

        var tareaServiceMock = new Mock<ITareaService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var formVm = new TareaFormViewModel(tareaServiceMock.Object, sessionMock.Object, navMock.Object, confirmMock.Object);
        inicializador!(formVm);

        Assert.Equal("Reponer stock depósito B", formVm.Titulo);
    }

    [Fact]
    public void VerTodasLasTareas_NavegaATareaListViewModel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, navMock, _, _) = Crear(usuario);

        vm.VerTodasLasTareasCommand.Execute(null);

        navMock.Verify(n => n.Navegar<TareaListViewModel>(), Times.Once);
    }
}
