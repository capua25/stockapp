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
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Shell real con navegación real: el ítem activo del sidebar (:activo) sigue a la pantalla mostrada aunque la
/// navegación no venga del sidebar (login, "Ver todas las tareas" de Inicio). Operador de permisos mixtos.
/// </summary>
public class ShellSidebarSigueNavegacionTests : IDisposable
{
    private readonly List<Window> _ventanas = [];

    public void Dispose()
    {
        foreach (var ventana in _ventanas)
        {
            ventana.Close();
        }
    }

    private sealed class FinanzasSinUsoFake : IFinanzasVistasService
    {
        public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes) => throw new NotSupportedException();
        public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotSupportedException();
        public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio) => throw new NotSupportedException();
        public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) => throw new NotSupportedException();
    }

    private sealed class BackupsSinUsoFake : IBackupsService
    {
        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task IniciarAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                xmlns:app="clr-namespace:StockApp.Presentation;assembly=GestionMunicipal"
                Width="1280" Height="760">
            <!-- Como App.axaml: el contenido del shell se resuelve con el ViewLocator. -->
            <Window.DataTemplates>
                <app:ViewLocator />
            </Window.DataTemplates>
            <vistas:ShellMainView />
        </Window>
        """;

    private (Window Ventana, NavigationService Navegacion) Montar()
    {
        // Mixto: Productos y Tareas sí; Finanzas y Reportes no (sus ítems quedan ocultos).
        var sesion = new SesionFake(new UsuarioSesion(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez"),
            Permisos.GestionarProductos, Permisos.GestionarTareas);
        var vencida = new Tarea
        {
            Id = 1, Titulo = "Reparar bache", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
            CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, FechaLimite = DateTime.UtcNow.Date.AddDays(-5),
        };
        var tareas = new TareaServiceFake([vencida]);
        NavigationService? navegacion = null;
        navegacion = new NavigationService(tipo => tipo == typeof(InicioViewModel)
            ? new InicioViewModel(sesion, navegacion!, new FinanzasSinUsoFake(), new BackupsSinUsoFake(), tareas, new AuthServiceFake())
            : tipo == typeof(TareaListViewModel)
                ? new TareaListViewModel(tareas, sesion, navegacion!, new ConfirmacionServiceFake())
                : throw new NotSupportedException(tipo.Name));
        var shell = new ShellMainViewModel(sesion, navegacion, new InfoAppFake("0.4.0"), new ConfirmacionServiceFake(),
            new AuthServiceFake(), new PreferenciasSidebarFake());
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        _ventanas.Add(ventana);
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return (ventana, navegacion);
    }

    private static string[] ItemsActivos(Window ventana) =>
        ventana.GetVisualDescendants().OfType<ItemSidebar>()
            .Where(i => i.Classes.Contains(":activo"))
            .Select(i => i.Titulo ?? "")
            .ToArray();

    private static void Asentar()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
    }

    [AvaloniaFact]
    public void AlLoguear_InicioSeVeActivo()
    {
        var (ventana, navegacion) = Montar();
        Assert.Empty(ItemsActivos(ventana)); // precondición: antes de navegar no hay nada activo

        navegacion.Navegar<InicioViewModel>(); // lo que hace ShellViewModel tras el login
        Asentar();

        Assert.Equal(new[] { "Inicio" }, ItemsActivos(ventana));
    }

    [AvaloniaFact]
    public void VerTodasLasTareasDesdeInicio_MarcaTareas()
    {
        var (ventana, navegacion) = Montar();
        navegacion.Navegar<InicioViewModel>();
        Asentar();
        var enlace = ventana.GetVisualDescendants().OfType<Button>().First(b => b.Name == "EnlaceVerTodasLasTareas");
        Assert.True(ArbolVisual.EsVisibleEnArbol(enlace));

        var centro = enlace.TranslatePoint(new Point(enlace.Bounds.Width / 2, enlace.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseDown(centro, MouseButton.Left);
        ventana.MouseUp(centro, MouseButton.Left);
        Asentar();

        Assert.Single(ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.Tareas.TareaListView>());
        Assert.Equal(new[] { "Tareas" }, ItemsActivos(ventana));
    }
}
