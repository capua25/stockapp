using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Bug B3 (2026-10-09): al usuario se le quitó catalogo.productos desde otra sesión; el 403 refrescó
/// el cache de permisos pero el sidebar siguió mostrando Productos hasta la próxima navegación.
/// Acá se dispara el MISMO camino que App.axaml.cs cablea a ApiSession.AccesoRevocado
/// (ManejadorAccesoRevocado) contra la ShellMainView real, SIN simular ninguna navegación: lo que
/// custodia es que el 403 solo, sin navegar, oculte el ítem. Operador con permisos mixtos: Admin
/// cortocircuita el gate y no probaría nada.
/// </summary>
public class AccesoRevocadoSidebarTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1000" Height="800">
            <vistas:ShellMainView />
        </Window>
        """;

    private static (Window Window, ShellMainViewModel Vm, SesionFake Sesion, AuthServiceFake Auth) Montar(
        string[] permisosIniciales, IReadOnlySet<string> permisosTrasRefresco)
    {
        var sesion = new SesionFake(RolUsuario.Operador, permisosIniciales);
        var auth = new AuthServiceFake(sesion, permisosTrasRefresco);
        var vm = new ShellMainViewModel(
            sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(), auth,
            new PreferenciasSidebarFake());

        foreach (var grupo in vm.Grupos)
            grupo.EstaExpandido = true;

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, sesion, auth);
    }

    private static bool EsVisible(Window window, object comando)
    {
        var boton = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => ReferenceEquals(b.Command, comando));
        return boton is not null && ArbolVisual.EsVisibleEnArbol(boton);
    }

    [AvaloniaFact]
    public async Task Un403_SinNavegar_OcultaProductosDelSidebar_YDejaLoQueSigueConPermiso()
    {
        var (window, vm, sesion, _) = Montar(
            new[] { Permisos.GestionarProductos, Permisos.VerFinanzas },
            new HashSet<string> { Permisos.VerFinanzas }); // el servidor ya no trae catalogo.productos

        Assert.True(EsVisible(window, vm.NavProductosCommand), "Precondición: Productos visible al arrancar.");
        Assert.True(EsVisible(window, vm.NavGastosCommand), "Precondición: Gastos (VerFinanzas) visible al arrancar.");

        await ManejadorAccesoRevocado.ManejarAsync(
            sesion, new AuthServiceFake(sesion, new HashSet<string> { Permisos.VerFinanzas }),
            new ConfirmacionServiceFake(), vm.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(EsVisible(window, vm.NavProductosCommand),
            "Tras el 403 con el permiso revocado, Productos tiene que desaparecer del sidebar sin navegar.");
        Assert.True(EsVisible(window, vm.NavGastosCommand),
            "Lo que el usuario sigue teniendo no se toca.");
    }
}
