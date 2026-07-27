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
        private readonly SaludBackupDto _salud;
        public BackupsServiceFake(SaludBackupDto salud) => _salud = salud;

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => Task.FromResult(_salud);
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
    {
        var vm = new InicioViewModel(
            new CurrentSessionFake(usuario), new NavigationServiceFake(),
            new FinanzasVistasServiceFake(), new BackupsServiceFake(salud));

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    private static Border BuscarBorderAvisoBackup(Window window)
    {
        var texto = window.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text == "Backup de la base de datos");
        return texto.GetVisualAncestors().OfType<Border>().First();
    }

    [AvaloniaFact]
    public void Montar_AdminConBackupVencido_MuestraElBanner()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.True(vm.MostrarAvisoBackup);
        var border = BuscarBorderAvisoBackup(window);
        Assert.True(border.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_AdminConBackupAlDia_OcultaElBanner()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var salud = new SaludBackupDto(DateTime.UtcNow, false, 26);

        var (window, vm) = Montar(usuario, salud);

        Assert.False(vm.MostrarAvisoBackup);
        var border = BuscarBorderAvisoBackup(window);
        Assert.False(border.IsVisible);
    }
}
