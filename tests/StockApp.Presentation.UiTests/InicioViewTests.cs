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
        public CurrentSessionFake(UsuarioSesion usuario) => _usuario = usuario;

        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => _usuario;
        public RolUsuario? RolActual => _usuario.Rol;

        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException("No usado en este banco de pruebas.");
        public void CerrarSesion() => throw new NotSupportedException("No usado en este banco de pruebas.");
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
            new FinanzasVistasServiceFake(), backups, new TareaServiceFake());

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
}
