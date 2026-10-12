using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Calendario de pagos v2 con la View REAL: estructura, estados de carga, vacíos por sección, formato es-UY y
/// la columna "Registrar pago" con un operador de permisos mixtos, también tras un 403 en caliente.</summary>
public class CalendarioPagosViewV2Tests
{
    private static readonly CalendarioPagosDto Datos = new(
        [new(101, "Barraca Carmelo S.A.", "A-0004398", 18_450m, new DateOnly(2026, 9, 28), "Pendiente"),
         new(102, "Ferretería Industrial del Este", null, 6_120.50m, new DateOnly(2026, 10, 2), "Pendiente")],
        [new(103, "Distribuidora Eléctrica Colonia", "B-0012877", 9_870m, new DateOnly(2026, 10, 9), "Pendiente")],
        [],
        [new(106, "Corralón Sur", "A-0002210", new DateOnly(2026, 10, 5), 12_300m)]);

    private sealed record Montaje(Window Ventana, CalendarioPagosView Vista, CalendarioPagosViewModel Vm, FinanzasVistasFake Servicio);

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    private static Montaje Montar(FinanzasVistasFake servicio, SesionFake? sesion = null)
    {
        var vm = new CalendarioPagosViewModel(servicio, new GastoServiceSinUsoFake(),
            sesion ?? Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos), new NavigationServiceFake());
        var vista = new CalendarioPagosView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => m.Vista.FindControl<T>(nombre)!;
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);
    private static DataGrid[] GrillasDeFacturas(Montaje m) =>
        [Control<DataGrid>(m, "GrillaVencidas"), Control<DataGrid>(m, "Grilla7Dias"), Control<DataGrid>(m, "Grilla30Dias")];

    [AvaloniaFact]
    public void RuedaSobreUnaGrillaDeFacturas_ScrollealaPagina()
    {
        // Las grillas no tienen scroll propio (crecen con sus filas): la rueda sobre ellas tiene que llegar al
        // ScrollViewer de la página, si no el usuario queda atrapado con el puntero encima de una tabla.
        var vencidas = Enumerable.Range(1, 40)
            .Select(i => new FacturaCalendarioDto(200 + i, $"Proveedor {i}", $"A-{i:0000}", 1_000m + i, new DateOnly(2026, 9, 1), "Pendiente"))
            .ToArray();
        var m = Montar(new FinanzasVistasFake { Calendario = Datos with { Vencidas = vencidas } });
        var pagina = m.Vista.GetVisualDescendants().OfType<ScrollViewer>().First();
        var grilla = Control<DataGrid>(m, "GrillaVencidas");
        Assert.True(pagina.Extent.Height > pagina.Viewport.Height, "la página tiene que poder scrollear para que el test tenga sentido");
        Assert.Equal(0, pagina.Offset.Y);
        var centro = grilla.TranslatePoint(new Point(grilla.Bounds.Width / 2, 60), m.Ventana)!.Value;

        m.Ventana.MouseWheel(centro, new Vector(0, -3));
        Dispatcher.UIThread.RunJobs();

        Assert.True(pagina.Offset.Y > 0, $"la rueda sobre la grilla no scrolleó la página (Offset.Y = {pagina.Offset.Y})");
    }

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTitulo_YActualizarSecundario()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos });

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Calendario de pagos", Control<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Contains("secundario", Control<Button>(m, "BotonActualizar").Classes);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void ConDatos_TitulosConContador_FormatoEsUy_YVaciosSoloDondeNoHay()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos });

        Assert.Equal("Vencidas · 2", Control<TextBlock>(m, "TituloVencidas").Text);
        Assert.Equal("A vencer en 30 días · 0", Control<TextBlock>(m, "Titulo30Dias").Text);
        var textos = m.Vista.GetVisualDescendants().OfType<TextBlock>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();
        Assert.Contains("$ 18.450,00", textos);
        Assert.Contains("28/09/2026", textos);
        Assert.Contains("—", textos);
        Assert.Contains("05/10/2026", textos);
        Assert.True(Visible(Control<TextBlock>(m, "Vacias30Dias")));
        Assert.Equal("No hay facturas que venzan en los próximos 30 días", Control<TextBlock>(m, "Vacias30Dias").Text);
        Assert.False(Visible(Control<TextBlock>(m, "VaciasVencidas")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla30Dias")));
    }

    [AvaloniaFact]
    public void VenceDeVencidas_EnTonoPeligro()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos });

        var vence = Control<DataGrid>(m, "GrillaVencidas").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "28/09/2026");
        Assert.Contains("negativo", vence.Classes);
    }

    [AvaloniaFact]
    public void Cargando_SinDatos_UnaSolaCardConElIndicador()
    {
        var m = Montar(new FinanzasVistasFake { PendienteCalendario = new TaskCompletionSource<CalendarioPagosDto>() });

        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaCargando")));
        Assert.False(Visible(Control<StackPanel>(m, "Secciones")));
        Assert.False(Visible(Control<TextBlock>(m, "VaciasVencidas")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new FinanzasVistasFake { FallaCalendario = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<TarjetaContenido>(m, "TarjetaSinConexion")));
        Assert.False(Visible(Control<StackPanel>(m, "Secciones")));

        servicio.FallaCalendario = null;
        servicio.Calendario = Datos;
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<TarjetaContenido>(m, "TarjetaSinConexion")));
        Assert.Equal("Vencidas · 2", Control<TextBlock>(m, "TituloVencidas").Text);
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLasCards()
    {
        var m = Montar(new FinanzasVistasFake { FallaCalendario = new UnauthorizedAccessException("403") });

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.False(Visible(Control<StackPanel>(m, "Secciones")));
    }

    [AvaloniaFact]
    public void SinPagos_LaColumnaRegistrarPagoNoSeVeEnNingunaGrilla()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos }, Mixto(Permisos.VerFinanzas));

        Assert.All(GrillasDeFacturas(m), g => Assert.False(g.Columns[^1].IsVisible));
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(),
            b => (b.Content as string) == "Registrar pago" && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public void ConPagos_LaColumnaSeVe()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos }, Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos));

        Assert.All(GrillasDeFacturas(m), g => Assert.True(g.Columns[^1].IsVisible));
        Assert.Contains(m.Vista.GetVisualDescendants().OfType<Button>(),
            b => (b.Content as string) == "Registrar pago" && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaPagos_OcultaLaColumnaSinNavegar()
    {
        var sesion = Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos);
        var tras = new System.Collections.Generic.HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new CalendarioPagosViewModel(new FinanzasVistasFake { Calendario = Datos }, new GastoServiceSinUsoFake(), sesion,
            new NavigationServiceFake());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<CalendarioPagosView>().Single();
        Assert.True(vista.FindControl<DataGrid>("GrillaVencidas")!.Columns[^1].IsVisible, "Precondición: con finanzas.pagos la columna se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vista.FindControl<DataGrid>("GrillaVencidas")!.Columns[^1].IsVisible);
        Assert.False(vista.FindControl<DataGrid>("Grilla7Dias")!.Columns[^1].IsVisible);
        Assert.Same(vm, shell.CurrentContent);
    }
}
