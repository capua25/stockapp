using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta InicioView real (mismo patron que MantenimientoViewTests.cs: VM real + fakes hechos a
/// mano, sin Moq porque este proyecto no lo referencia) para confirmar que el aviso de salud de
/// backup (MostrarAvisoBackup/TextoAvisoBackup, Task 11) efectivamente se renderiza — el gap que
/// motivo este fix es que el ViewModel estaba correcto pero ningun XAML lo bindeaba.
/// </summary>
public class InicioViewTests
{
    private sealed class CurrentSessionFake : ICurrentSession
    {
        private readonly UsuarioSesion _usuario;
        private readonly IReadOnlySet<string> _permisos;

        public CurrentSessionFake(UsuarioSesion usuario) : this(usuario, new HashSet<string>()) { }

        /// <summary>
        /// Overload con permisos configurables (fix bug cosmético 2026-08-17): los tests de
        /// gating del panel "Accesos rápidos" necesitan un Operador con EXACTAMENTE un permiso
        /// (ej. solo VerReportes), no el vacío fijo del constructor original.
        /// </summary>
        public CurrentSessionFake(UsuarioSesion usuario, IReadOnlySet<string> permisos)
        {
            _usuario = usuario;
            _permisos = permisos;
        }

        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => _usuario;
        public RolUsuario? RolActual => _usuario.Rol;
        public IReadOnlySet<string> PermisosActuales => _permisos;
        public void EstablecerPermisos(IReadOnlySet<string> permisos) { }

        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public void CerrarSesion() => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    /// <summary>
    /// Fake mínimo de IAuthService (review Ronda 1, Task 14): InicioViewModel lo necesita para
    /// refrescar permisos al entrar a la pantalla (CargarAsync). ObtenerPermisosPropiosAsync
    /// devuelve vacío -- ninguno de estos tests ejercita el refresco en sí, solo necesitan que
    /// no rompa (RefrescoPermisos.DispararBestEffortAsync ya absorbe cualquier excepción, así
    /// que ni siquiera hace falta que este fake tenga éxito).
    /// </summary>
    private sealed class AuthServiceFake : IAuthService
    {
        public Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task LogoutAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync()
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
            => Task.FromResult(new CalendarioPagosDto(
                Array.Empty<FacturaCalendarioDto>(), Array.Empty<FacturaCalendarioDto>(),
                Array.Empty<FacturaCalendarioDto>(), Array.Empty<PagoRecienteDto>()));
    }

    private sealed class BackupsServiceFake : IBackupsService
    {
        private readonly SaludBackupDto? _salud;
        private readonly Exception? _excepcion;

        public BackupsServiceFake(SaludBackupDto salud) => _salud = salud;
        public BackupsServiceFake(Exception excepcion) => _excepcion = excepcion;

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) =>
            _excepcion is not null ? Task.FromException<SaludBackupDto>(_excepcion) : Task.FromResult(_salud!);

        public Task IniciarAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=StockApp.Presentation"
                Width="900" Height="700">
            <vistas:InicioView />
        </Window>
        """;

    private static (Window Window, InicioViewModel Vm) Montar(UsuarioSesion usuario, SaludBackupDto salud)
        => Montar(usuario, new BackupsServiceFake(salud));

    private static (Window Window, InicioViewModel Vm) Montar(UsuarioSesion usuario, IBackupsService backups)
    {
        var vm = new InicioViewModel(
            new CurrentSessionFake(usuario), new NavigationServiceFake(),
            new FinanzasVistasServiceFake(), backups, new TareaServiceFake(), new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    // Tercer estado (review final E1): ahora hay DOS Border con el mismo texto "Backup de la
    // base de datos" (Problema y Desconocido, InicioView.axaml), uno oculto y otro visible según
    // el caso — buscar por el TextBlock hijo ya no alcanza para desambiguar. x:Name también
    // pobla StyledElement.Name, así que se busca por ahí directamente en el árbol visual (NO por
    // Window.FindControl: InicioView es un UserControl separado con su propio NameScope, el
    // Window de este test no lo alcanza).
    private static Border BuscarBorderPorNombre(Window window, string nombre)
        => window.GetVisualDescendants().OfType<Border>().First(b => b.Name == nombre);

    [AvaloniaFact]
    public void Montar_AdminConBackupVencido_MuestraElBanner()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.MostrarAvisoBackup);
        Assert.True(vm.MostrarAvisoBackupProblema);
        var borderProblema = BuscarBorderPorNombre(window, "BorderAvisoBackupProblema");
        Assert.True(borderProblema.IsVisible);
        var borderDesconocido = BuscarBorderPorNombre(window, "BorderAvisoBackupDesconocido");
        Assert.False(borderDesconocido.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_AdminConBackupAlDia_OcultaElBanner()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.False(vm.MostrarAvisoBackup);
        var borderProblema = BuscarBorderPorNombre(window, "BorderAvisoBackupProblema");
        Assert.False(borderProblema.IsVisible);
        var borderDesconocido = BuscarBorderPorNombre(window, "BorderAvisoBackupDesconocido");
        Assert.False(borderDesconocido.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_ServicioDeBackupFalla_MuestraElBannerDeEstadoDesconocido()
    {
        // Tercer estado (review final E1): API caída / 403 / 404 de una versión vieja del
        // servidor — el banner correcto acá es "no se pudo verificar", NUNCA ni "al día"
        // (ocultarlo) ni "vencido" (el border rojo de Problema).
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (window, vm) = Montar(usuario, new BackupsServiceFake(new InvalidOperationException("servidor caído")));

        Assert.True(vm.MostrarAvisoBackup);
        Assert.True(vm.MostrarAvisoBackupDesconocido);
        Assert.False(vm.MostrarAvisoBackupProblema);

        var borderDesconocido = BuscarBorderPorNombre(window, "BorderAvisoBackupDesconocido");
        Assert.True(borderDesconocido.IsVisible);
        var borderProblema = BuscarBorderPorNombre(window, "BorderAvisoBackupProblema");
        Assert.False(borderProblema.IsVisible);
    }

    // ── Gating de accesos rápidos operativos (fix bug de coherencia de permisos, 2026-08-16) ──
    // La auditoría encontró el bug en el XAML, no en el ViewModel: PuedeGestionarProductos/
    // PuedeRegistrarEntradaSalida/PuedeRegistrarMovimientos podían existir perfectamente en
    // InicioViewModel y el binding igual faltar en InicioView.axaml -- un test de VM puro no
    // hubiese detectado eso. Estos tests montan la vista REAL para probar el binding IsVisible
    // de punta a punta, mismo motivo que el resto de esta clase (ver comentario de la clase).

    private static Button BuscarBotonPorNombre(Window window, string nombre)
        => window.GetVisualDescendants().OfType<Button>().First(b => b.Name == nombre);

    [AvaloniaFact]
    public void Montar_OperadorSinPermisos_OcultaLosCuatroAccesosRapidosOperativos()
    {
        var usuario = new UsuarioSesion(2, "operador", RolUsuario.Operador, "Juan Pérez");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.False(vm.PuedeGestionarProductos);
        Assert.False(vm.PuedeRegistrarEntradaSalida);
        Assert.False(vm.PuedeRegistrarMovimientos);

        Assert.False(BuscarBotonPorNombre(window, "BotonAccesoProductos").IsVisible);
        Assert.False(BuscarBotonPorNombre(window, "BotonAccesoRegistrarEntrada").IsVisible);
        Assert.False(BuscarBotonPorNombre(window, "BotonAccesoRegistrarSalida").IsVisible);
        Assert.False(BuscarBotonPorNombre(window, "BotonAccesoHistorialMovimientos").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_AdminConTodosLosPermisos_MuestraLosCuatroAccesosRapidosOperativos()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.PuedeGestionarProductos);
        Assert.True(vm.PuedeRegistrarEntradaSalida);
        Assert.True(vm.PuedeRegistrarMovimientos);

        Assert.True(BuscarBotonPorNombre(window, "BotonAccesoProductos").IsVisible);
        Assert.True(BuscarBotonPorNombre(window, "BotonAccesoRegistrarEntrada").IsVisible);
        Assert.True(BuscarBotonPorNombre(window, "BotonAccesoRegistrarSalida").IsVisible);
        Assert.True(BuscarBotonPorNombre(window, "BotonAccesoHistorialMovimientos").IsVisible);
    }

    // ── Gating del panel completo "Accesos rápidos" (fix bug cosmético, 2026-08-17) ──
    // El Border que envuelve las 6 tarjetas no tenía IsVisible propio -- si el usuario no
    // cumplía NINGUNO de los 6 gates de las tarjetas, éstas se ocultaban pero el card y el
    // título "Accesos rápidos" quedaban visibles, vacíos (reproducible con opfinanzas, que solo
    // tiene VerFinanzas). Mismo motivo que la sección de arriba para montar la vista REAL: un
    // test de VM puro (PuedeVerAccesosRapidos) no detecta que el binding falte en el XAML.

    [AvaloniaFact]
    public void Montar_OperadorSinNingunPermisoDeAccesoRapido_OcultaElPanelCompleto()
    {
        var usuario = new UsuarioSesion(2, "opfinanzas", RolUsuario.Operador, "Op Finanzas");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.False(vm.PuedeVerAccesosRapidos);
        Assert.False(BuscarBorderPorNombre(window, "BorderAccesosRapidos").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorConSoloVerReportes_MuestraElPanelCompleto()
    {
        var usuario = new UsuarioSesion(2, "opreportes", RolUsuario.Operador, "Op Reportes");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var vm = new InicioViewModel(
            new CurrentSessionFake(usuario, new HashSet<string> { Permisos.VerReportes }),
            new NavigationServiceFake(), new FinanzasVistasServiceFake(), new BackupsServiceFake(salud),
            new TareaServiceFake(), new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.PuedeVerAccesosRapidos);
        Assert.True(BuscarBorderPorNombre(window, "BorderAccesosRapidos").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_Admin_MuestraElPanelCompleto()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.PuedeVerAccesosRapidos);
        Assert.True(BuscarBorderPorNombre(window, "BorderAccesosRapidos").IsVisible);
    }
}
