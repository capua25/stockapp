using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
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
using StockApp.Presentation.Controls.V2;
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
    private sealed class FinanzasVistasServiceFake : IFinanzasVistasService
    {
        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(Caja caja, int anio, int mes)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(Caja caja, int anio)
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
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="900" Height="700">
            <vistas:InicioView />
        </Window>
        """;

    private static (Window Window, InicioViewModel Vm) Montar(UsuarioSesion usuario, SaludBackupDto salud)
        => Montar(usuario, new BackupsServiceFake(salud));

    private static (Window Window, InicioViewModel Vm) Montar(UsuarioSesion usuario, IBackupsService backups)
    {
        var vm = new InicioViewModel(
            new SesionFake(usuario), new NavigationServiceFake(),
            new FinanzasVistasServiceFake(), backups, new TareaServiceFake(), new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    // Los avisos de backup son `Alerta` con nombre propio desde la UI v2 (AlertaBackupVencido y
    // AlertaBackupDesconocido, InicioView.axaml), uno oculto y otro visible según el caso. x:Name también
    // pobla StyledElement.Name, así que se busca por ahí directamente en el árbol visual (NO por
    // Window.FindControl: InicioView es un UserControl separado con su propio NameScope, el
    // Window de este test no lo alcanza).
    private static Control BuscarPorNombre(Window window, string nombre) => window.GetVisualDescendants().OfType<Control>().First(c => c.Name == nombre);

    [AvaloniaFact]
    public void Montar_AdminConBackupVencido_MuestraLaAlertaDeError()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.MostrarAvisoBackupProblema);
        var vencido = Assert.IsType<Alerta>(BuscarPorNombre(window, "AlertaBackupVencido"));
        Assert.True(vencido.IsVisible);
        Assert.Equal(VarianteAlerta.Error, vencido.Variante);
        Assert.False(BuscarPorNombre(window, "AlertaBackupDesconocido").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_AdminConBackupAlDia_NoMuestraNingunaAlerta()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.False(vm.MostrarAvisoBackup);
        Assert.False(BuscarPorNombre(window, "AlertaBackupVencido").IsVisible);
        Assert.False(BuscarPorNombre(window, "AlertaBackupDesconocido").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_ServicioDeBackupFalla_MuestraLaAlertaDeAdvertencia()
    {
        // Tercer estado (review final E1): "no se pudo verificar" NUNCA se ve como "vencido" (error).
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (window, vm) = Montar(usuario, new BackupsServiceFake(new InvalidOperationException("servidor caído")));

        Assert.True(vm.MostrarAvisoBackupDesconocido);
        var desconocido = Assert.IsType<Alerta>(BuscarPorNombre(window, "AlertaBackupDesconocido"));
        Assert.True(desconocido.IsVisible);
        Assert.Equal(VarianteAlerta.Advertencia, desconocido.Variante);
        Assert.False(BuscarPorNombre(window, "AlertaBackupVencido").IsVisible);
    }

    // ── Gating de accesos rápidos operativos (fix bug de coherencia de permisos, 2026-08-16) ──
    // La auditoría encontró el bug en el XAML, no en el ViewModel: PuedeGestionarProductos/
    // PuedeRegistrarEntradaSalida/PuedeRegistrarMovimientos podían existir perfectamente en
    // InicioViewModel y el binding igual faltar en InicioView.axaml -- un test de VM puro no
    // hubiese detectado eso. Estos tests montan la vista REAL para probar el binding IsVisible
    // de punta a punta, mismo motivo que el resto de esta clase (ver comentario de la clase).

    // Árbol LÓGICO, no visual: desde la UI v2 los accesos viven dentro de una TarjetaContenido
    // (control con plantilla); si el panel entero está oculto la plantilla no se aplica y las
    // tarjetas nunca llegan al árbol visual. Se mira el IsVisible propio de cada tarjeta, que es
    // el binding del gate.
    private static Button BuscarBotonPorNombre(Window window, string nombre)
        => window.GetLogicalDescendants().OfType<Button>().First(b => b.Name == nombre);

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
        Assert.False(BuscarPorNombre(window, "PanelAccesosRapidos").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_OperadorConSoloVerReportes_MuestraElPanelCompleto()
    {
        var usuario = new UsuarioSesion(2, "opreportes", RolUsuario.Operador, "Op Reportes");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var vm = new InicioViewModel(
            new SesionFake(usuario, Permisos.VerReportes),
            new NavigationServiceFake(), new FinanzasVistasServiceFake(), new BackupsServiceFake(salud),
            new TareaServiceFake(), new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.PuedeVerAccesosRapidos);
        Assert.True(BuscarPorNombre(window, "PanelAccesosRapidos").IsVisible);
    }

    [AvaloniaFact]
    public void Montar_Admin_MuestraElPanelCompleto()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.PuedeVerAccesosRapidos);
        Assert.True(BuscarPorNombre(window, "PanelAccesosRapidos").IsVisible);
    }
}
