using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Sidebar v2 (spec §4.1) montado con la vista real. Los gates por ítem los custodia ShellMainViewGatesTests.</summary>
public class ShellMainViewV2Tests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                Width="1280" Height="760">
            <vistas:ShellMainView />
        </Window>
        """;

    private static readonly UsuarioSesion Admin = new(1, "mperez", RolUsuario.Admin, "María Pérez");
    private static readonly UsuarioSesion Operador = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");

    private static (Window Ventana, ShellMainViewModel Vm, NavigationServiceFake Navegacion) Montar(
        UsuarioSesion usuario, string[] permisos, IReadOnlySet<string>? permisosTrasRefresco = null)
    {
        var sesion = new SesionFake(usuario, permisos);
        var navegacion = new NavigationServiceFake();
        var auth = permisosTrasRefresco is null ? new AuthServiceFake() : new AuthServiceFake(sesion, permisosTrasRefresco);
        var vm = new ShellMainViewModel(sesion, navegacion, new InfoAppFake("0.4.0"), new ConfirmacionServiceFake(), auth,
            new PreferenciasSidebarFake());
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        ventana.DataContext = vm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (ventana, vm, navegacion);
    }

    private static T Nombrado<T>(Window ventana, string nombre) where T : Control =>
        ventana.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static string[] HeadersVisibles(Window ventana) =>
        ventana.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("header-seccion-sidebar") && ArbolVisual.EsVisibleEnArbol(t))
            .Select(t => t.Text!)
            .ToArray();

    [AvaloniaFact]
    public void Ui2_VaEnElSidebarYNoEnLaRaiz()
    {
        var (ventana, _, _) = Montar(Admin, []);

        Assert.Contains("ui2", Nombrado<Border>(ventana, "Sidebar").Classes);
        var vista = ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.ShellMainView>().Single();
        Assert.DoesNotContain("ui2", vista.Classes);
    }

    [AvaloniaFact]
    public void ContenidoHospedado_NoTieneAncestroUi2()
    {
        // Review Focus 1: todo lo que hospeda el ContentControl hereda de sus ancestros. Si alguno
        // tiene ui2, las vistas todavía no migradas toman los estilos v2.
        var (ventana, _, _) = Montar(Admin, []);
        var contenido = Nombrado<ContentControl>(ventana, "Contenido");

        Assert.DoesNotContain(contenido.GetSelfAndVisualAncestors().OfType<StyledElement>(), e => e.Classes.Contains("ui2"));
    }

    [AvaloniaFact]
    public void Admin_VeLasCincoSecciones() =>
        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "GESTIÓN", "ANÁLISIS", "ADMINISTRACIÓN" }, HeadersVisibles(Montar(Admin, []).Ventana));

    [AvaloniaFact]
    public void OperadorMixto_SeccionesSinItemsVisibles_OcultanSuHeader()
    {
        var (ventana, _, _) = Montar(Operador,
            [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas]);

        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "GESTIÓN" }, HeadersVisibles(ventana));
    }

    [AvaloniaFact]
    public void RevocacionEnCaliente_OcultaElHeaderDeLaSeccionQueSeVacia()
    {
        var (ventana, _, navegacion) = Montar(Operador, [Permisos.VerReportes], new HashSet<string>());
        Assert.Equal(new[] { "ANÁLISIS" }, HeadersVisibles(ventana));

        navegacion.SimularNavegacion();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(HeadersVisibles(ventana));
    }

    [AvaloniaFact]
    public void Pie_MuestraInicialesNombreRolYVersion()
    {
        var (ventana, _, _) = Montar(Admin, []);

        Assert.Equal("MP", Nombrado<TextBlock>(ventana, "Iniciales").Text);
        Assert.Equal("María Pérez", Nombrado<TextBlock>(ventana, "NombreUsuario").Text);
        Assert.Equal("Administrador", Nombrado<TextBlock>(ventana, "RolUsuario").Text);
        Assert.Contains(ventana.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "v0.4.0" && t.Classes.Contains("version"));
    }

    [AvaloniaFact]
    public void Marca_LlevaLaLineaMunicipioDeCarmelo()
    {
        // P6: la línea va bajo "Gestión Municipal" y con la clase tipográfica del subtítulo.
        var (ventana, _, _) = Montar(Admin, []);

        var textos = ventana.GetVisualDescendants().OfType<TextBlock>().ToList();
        var marca = textos.Single(t => t.Classes.Contains("marca"));
        var subtitulo = Assert.Single(textos, t => t.Classes.Contains("marca-subtitulo"));
        Assert.Equal("Municipio de Carmelo", subtitulo.Text);
        Assert.True(subtitulo.TranslatePoint(default, ventana)!.Value.Y > marca.TranslatePoint(default, ventana)!.Value.Y);
    }

    [AvaloniaFact]
    public void CerrarSesion_UsaElComandoConConfirmacionDelVm()
    {
        var (ventana, vm, _) = Montar(Admin, []);
        var boton = Nombrado<Button>(ventana, "CerrarSesion");

        Assert.Same(vm.CerrarSesionCommand, boton.Command);
        Assert.Equal("Cerrar sesión", ToolTip.GetTip(boton));
    }

    [AvaloniaFact]
    public void Navegar_MarcaActivoSoloElItemDeLaSeccion()
    {
        var (ventana, vm, _) = Montar(Admin, []);

        vm.NavProductosCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        var activos = ventana.GetVisualDescendants().OfType<ItemSidebar>().Where(i => i.EstaActivo).ToList();
        Assert.Same(vm.NavProductosCommand, Assert.Single(activos).Command);
    }

    [AvaloniaFact]
    public void SinAcordeones_LosItemsSeVenAunqueElGrupoEsteColapsado()
    {
        // Spec §4.1: secciones siempre visibles. EstaExpandido sigue en el VM (deuda declarada)
        // pero la vista no lo usa.
        var (ventana, vm, _) = Montar(Admin, []);
        foreach (var grupo in vm.Grupos)
        {
            grupo.EstaExpandido = false;
        }

        Dispatcher.UIThread.RunJobs();

        var productos = ventana.GetVisualDescendants().OfType<ItemSidebar>().Single(i => ReferenceEquals(i.Command, vm.NavProductosCommand));
        Assert.True(ArbolVisual.EsVisibleEnArbol(productos));
    }
}
