using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views.Reportes;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Hub de Reportes v2 con la View REAL dentro de ShellMainView, con operadores de permisos mixtos (Admin cortocircuita
/// el gate): tarjetas, gates por tarjeta, click, sidebar y revocación en caliente por el camino real.</summary>
public class ReportesViewV2Tests
{
    private static readonly string[] Titulos =
    [
        "Valorización de inventario", "Stock por categoría", "Historial por producto",
        "Productos más movidos", "Log de auditoría", "Estadística de tareas",
    ];

    private static readonly string[] Nombres =
    [
        "TarjetaValorizacion", "TarjetaStockCategoria", "TarjetaHistorialPorProducto",
        "TarjetaMasMovidos", "TarjetaAuditoria", "TarjetaEstadisticaTareas",
    ];

    private sealed record Montaje(Window Ventana, ShellMainViewModel Shell, ReportesViewModel Hub, ReportesView Vista, SesionFake Sesion, NavigationServiceFake Navegacion);

    private static Montaje Montar(SesionFake sesion, IReadOnlySet<string>? permisosTrasRevocar = null)
    {
        var navegacion = new NavigationServiceFake();
        var shell = new ShellMainViewModel(sesion, navegacion, new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, permisosTrasRevocar ?? new HashSet<string>()), new PreferenciasSidebarFake());
        foreach (var grupo in shell.Grupos)
            grupo.EstaExpandido = true;
        shell.SeccionActiva = "Reportes";
        var hub = new ReportesViewModel(sesion, navegacion);
        shell.CurrentContent = hub;

        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<ReportesView>().Single();
        return new Montaje(ventana, shell, hub, vista, sesion, navegacion);
    }

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    private static TarjetaAcceso Tarjeta(Montaje m, string nombre) => m.Vista.FindControl<TarjetaAcceso>(nombre)!;
    private static bool Visible(Montaje m, string nombre) => ArbolVisual.EsVisibleEnArbol(Tarjeta(m, nombre));

    [AvaloniaFact]
    public void Raiz_TieneUi2_TituloReportes_SinPrimario_SinBreadcrumb()
    {
        var m = Montar(Mixto(Permisos.VerReportes));

        Assert.Contains("ui2", m.Vista.Classes);
        var encabezado = m.Vista.FindControl<EncabezadoPagina>("Encabezado")!;
        Assert.Equal("Reportes", encabezado.Titulo);
        Assert.Null(encabezado.BreadcrumbOrigen);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Admin_SeisTarjetas_EnElOrdenDeLaSpec()
    {
        var m = Montar(new SesionFake(RolUsuario.Admin));

        var visibles = m.Vista.GetVisualDescendants().OfType<TarjetaAcceso>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Titulo).ToList();
        Assert.Equal(Titulos, visibles);
        foreach (var (nombre, titulo) in Nombres.Zip(Titulos))
            Assert.Equal(titulo, Tarjeta(m, nombre).Titulo);
    }

    [AvaloniaFact]
    public void OperadorConSoloVerReportes_CincoTarjetas_SinHistorial()
    {
        var m = Montar(Mixto(Permisos.VerReportes));

        foreach (var nombre in Nombres.Where(n => n != "TarjetaHistorialPorProducto"))
            Assert.True(Visible(m, nombre), nombre);
        Assert.False(Visible(m, "TarjetaHistorialPorProducto"));
    }

    [AvaloniaFact]
    public void OperadorConVerReportesYMovimientosSinCatalogo_NoVeHistorialPorProducto()
    {
        // Review Focus 3: el buscador de producto pega a GET /productos (catalogo.productos); sin ese permiso cada búsqueda daría 403.
        var m = Montar(Mixto(Permisos.VerReportes, Permisos.RegistrarMovimientos));

        Assert.False(Visible(m, "TarjetaHistorialPorProducto"));
        Assert.True(Visible(m, "TarjetaValorizacion"));
    }

    [AvaloniaFact]
    public void OperadorConVerReportesMovimientosYCatalogo_VeHistorialPorProducto()
    {
        var m = Montar(Mixto(Permisos.VerReportes, Permisos.RegistrarMovimientos, Permisos.GestionarProductos));

        Assert.True(Visible(m, "TarjetaHistorialPorProducto"));
    }

    [AvaloniaFact]
    public void ClickEnUnaTarjeta_NavegaAlReporte_YElSidebarSigueEnReportes()
    {
        var m = Montar(Mixto(Permisos.VerReportes));

        MontajeV2.Clic(m.Ventana, Tarjeta(m, "TarjetaAuditoria"));

        Assert.Equal(typeof(AuditoriaLogViewModel), m.Navegacion.UltimoDestino);
        Assert.Equal("Reportes", m.Shell.SeccionActiva);
        var item = m.Shell.Grupos.Single(g => g.Titulo == "ANÁLISIS").Items.Single();
        Assert.Equal("Reportes", item.Titulo);
        Assert.True(item.EstaActivo);
    }

    [AvaloniaFact]
    public async Task RevocarVerReportes_DentroDelShell_LasTarjetasYElItemDesaparecen()
    {
        var sesion = Mixto(Permisos.VerReportes, Permisos.RegistrarMovimientos);
        var m = Montar(sesion, new HashSet<string> { Permisos.RegistrarMovimientos });
        Assert.True(Visible(m, "TarjetaAuditoria"), "Precondición: con reportes.ver la tarjeta se ve.");
        Assert.True(m.Shell.Grupos.Single(g => g.Titulo == "ANÁLISIS").Items.Single().EsVisible);

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, new HashSet<string> { Permisos.RegistrarMovimientos }),
            new ConfirmacionServiceFake(), m.Shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        foreach (var nombre in Nombres)
            Assert.False(Visible(m, nombre), nombre);
        Assert.False(m.Shell.Grupos.Single(g => g.Titulo == "ANÁLISIS").Items.Single().EsVisible);
    }

    [AvaloniaFact]
    public void HistorialOculto_NoDejaHueco()
    {
        var m = Montar(Mixto(Permisos.VerReportes));

        var visibles = m.Vista.GetVisualDescendants().OfType<TarjetaAcceso>().Where(ArbolVisual.EsVisibleEnArbol).ToList();
        Assert.Equal(5, visibles.Count);
        // Fila 1: tres; la cuarta visible (Log de auditoría) abre la fila 2 en la columna 1.
        var primera = visibles[0].TranslatePoint(default, m.Vista)!.Value;
        var cuarta = visibles[3].TranslatePoint(default, m.Vista)!.Value;
        Assert.Equal("Log de auditoría", visibles[3].Titulo);
        Assert.Equal(primera.X, cuarta.X, 1);
        Assert.True(cuarta.Y > primera.Y);
    }
}
