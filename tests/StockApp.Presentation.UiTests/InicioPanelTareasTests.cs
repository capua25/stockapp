using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta InicioView real (mismo patrón que InicioViewTests.cs) para el panel "Tareas que
/// requieren atención" (spec 2026-08-06): que efectivamente se renderice con las filas correctas,
/// que NO se renderice cuando no hay nada para mostrar, que un click real navegue al detalle de
/// la fila CORRECTA (no solo a un tipo correcto), y que el gating por rol funcione contra el
/// árbol visual real (un Operador no ve las tareas tomadas por otro operador).
/// </summary>
public class InicioPanelTareasTests
{
    private sealed class CurrentSessionFake : ICurrentSession
    {
        private readonly UsuarioSesion _usuario;
        public CurrentSessionFake(UsuarioSesion usuario) => _usuario = usuario;

        public bool EstaAutenticado => true;
        public UsuarioSesion? UsuarioActual => _usuario;
        public RolUsuario? RolActual => _usuario.Rol;
        public IReadOnlySet<string> PermisosActuales => new HashSet<string>();
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
        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");

        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default)
            => Task.FromResult(new SaludBackupDto(DateTime.UtcNow, false, 26));

        public Task IniciarAsync(CancellationToken ct = default)
            => throw new NotSupportedException("No usado en este banco de pruebas.");
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=StockApp.Presentation"
                Width="900" Height="900">
            <vistas:InicioView />
        </Window>
        """;

    private static Tarea TareaCon(
        int id, string titulo, DateTime? fechaLimite, EstadoTarea estado = EstadoTarea.Pendiente,
        int? tomadaPorUsuarioId = null) => new()
    {
        Id = id, Titulo = titulo, Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = fechaLimite,
        TomadaPorUsuarioId = tomadaPorUsuarioId,
    };

    private static (Window Window, InicioViewModel Vm, NavigationRecorderFake Navegacion) Montar(
        UsuarioSesion usuario, List<Tarea> tareas)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = new InicioViewModel(
            new CurrentSessionFake(usuario), navegacion, new FinanzasVistasServiceFake(),
            new BackupsServiceFake(), new TareaServiceFake(tareas), new AuthServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm, navegacion);
    }

    private static void Clickear(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var centro = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var puntoEnVentana = control.TranslatePoint(centro, window) ?? centro;
        window.MouseMove(puntoEnVentana);
        window.MouseDown(puntoEnVentana, MouseButton.Left);
        window.MouseUp(puntoEnVentana, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Border BuscarBorderPorNombre(Window window, string nombre)
        => window.GetVisualDescendants().OfType<Border>().First(b => b.Name == nombre);

    private static bool IsVisibleEnArbol(Visual visual)
    {
        for (Visual? actual = visual; actual is not null; actual = actual.GetVisualParent())
        {
            if (actual is Control c && !c.IsVisible) return false;
        }
        return true;
    }

    [AvaloniaFact]
    public void Montar_ConTareasVencidasYProximas_MuestraElPanelConLasFilasCorrectas()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "Reponer stock depósito B", DateTime.UtcNow.Date.AddDays(-3)),
            TareaCon(2, "Controlar vencimientos", DateTime.UtcNow.Date.AddDays(-1)),
            TareaCon(3, "Inventario estantería 4", DateTime.UtcNow.Date),
        };
        var (window, _, _) = Montar(usuario, tareas);

        var border = BuscarBorderPorNombre(window, "BorderPanelTareas");
        Assert.True(border.IsVisible);

        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Reponer stock depósito B", textos);
        Assert.Contains("Controlar vencimientos", textos);
        Assert.Contains("Inventario estantería 4", textos);
        Assert.Contains("VENCIDAS (2)", textos);
        Assert.Contains("PRÓXIMAS A VENCER (1)", textos);
    }

    /// <summary>
    /// El hueco explícito que pidió el encargo: "si no hay nada que mostrar, el panel no se
    /// muestra" -- nunca un cartel de "no hay tareas vencidas" ensuciando Inicio todos los días.
    /// </summary>
    [AvaloniaFact]
    public void Montar_SinTareasQueRequieranAtencion_NoRenderizaElPanel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (window, vm, _) = Montar(usuario, new List<Tarea>());

        Assert.False(vm.MostrarPanelTareas);
        var border = BuscarBorderPorNombre(window, "BorderPanelTareas");
        Assert.False(IsVisibleEnArbol(border));
    }

    /// <summary>
    /// Click real en una fila vencida: navega a TareaFormViewModel Y con la tarea CORRECTA (no
    /// solo con el tipo correcto) -- ejecuta el inicializador capturado contra un
    /// TareaFormViewModel real y confirma su título.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_EnFilaVencida_NavegaAlDetalleDeLaTareaCorrecta()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "Controlar vencimientos", DateTime.UtcNow.Date.AddDays(-1)),
            TareaCon(2, "Reponer stock depósito B", DateTime.UtcNow.Date.AddDays(-3)),
        };
        var (window, _, navegacion) = Montar(usuario, tareas);

        var filaBoton = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Reponer stock depósito B"));
        Clickear(window, filaBoton);

        Assert.Equal(typeof(TareaFormViewModel), navegacion.UltimoTipoNavegado);
        Assert.NotNull(navegacion.UltimoInicializadorTareaForm);

        var formVm = new TareaFormViewModel(
            new TareaServiceFake(), new TareaSessionFake(RolUsuario.Admin), navegacion, new ConfirmacionServiceFake());
        navegacion.UltimoInicializadorTareaForm!(formVm);

        Assert.Equal("Reponer stock depósito B", formVm.Titulo);
    }

    [AvaloniaFact]
    public void ClickReal_EnFilaProximaAVencer_NavegaAlDetalleDeLaTareaCorrecta()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "Inventario estantería 4", DateTime.UtcNow.Date),
            TareaCon(2, "Recibir factura proveedor", DateTime.UtcNow.Date.AddDays(2)),
        };
        var (window, _, navegacion) = Montar(usuario, tareas);

        var filaBoton = window.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Recibir factura proveedor"));
        Clickear(window, filaBoton);

        Assert.Equal(typeof(TareaFormViewModel), navegacion.UltimoTipoNavegado);
        var formVm = new TareaFormViewModel(
            new TareaServiceFake(), new TareaSessionFake(RolUsuario.Admin), navegacion, new ConfirmacionServiceFake());
        navegacion.UltimoInicializadorTareaForm!(formVm);

        Assert.Equal("Recibir factura proveedor", formVm.Titulo);
    }

    [AvaloniaFact]
    public void ClickReal_EnVerTodasLasTareas_NavegaATareaListViewModel()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea> { TareaCon(1, "x", DateTime.UtcNow.Date) };
        var (window, _, navegacion) = Montar(usuario, tareas);

        var boton = window.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, "Ver todas las tareas →"));
        Clickear(window, boton);

        Assert.Equal(typeof(TareaListViewModel), navegacion.UltimoTipoNavegado);
    }

    /// <summary>
    /// El gating de permisos por rol de este panel (spec: "un Operador no ve las tareas tomadas
    /// por otro operador") probado contra el ÁRBOL VISUAL real, no solo contra la colección del
    /// ViewModel -- mismo criterio que TareaListViewTests.Montar_RolOperador_NoMuestra...
    /// </summary>
    [AvaloniaFact]
    public void Montar_RolOperador_NoMuestraEnElArbolLasTareasTomadasPorOtroOperador()
    {
        var usuario = new UsuarioSesion(5, "jperez", RolUsuario.Operador, "Juan Pérez");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "De otro operador", DateTime.UtcNow.Date.AddDays(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 99),
            TareaCon(2, "Mía", DateTime.UtcNow.Date.AddDays(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 5),
            TareaCon(3, "De nadie", DateTime.UtcNow.Date, EstadoTarea.Pendiente),
        };
        var (window, _, _) = Montar(usuario, tareas);

        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.DoesNotContain("De otro operador", textos);
        Assert.Contains("Mía", textos);
        Assert.Contains("De nadie", textos);
    }

    [AvaloniaFact]
    public void Montar_RolAdmin_MuestraEnElArbolLasTareasDeCualquierOperador()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var tareas = new List<Tarea>
        {
            TareaCon(1, "De un operador", DateTime.UtcNow.Date.AddDays(-1), EstadoTarea.EnCurso, tomadaPorUsuarioId: 99),
        };
        var (window, _, _) = Montar(usuario, tareas);

        var textos = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("De un operador", textos);
    }
}
