using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Proyectos;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Proyectos;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>Listado y ficha de Proyectos dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto
/// en los dos renders. La ficha se scrollea hasta la sección Gastos (la que cambia con el presupuesto y con finanzas.ver).</summary>
[Trait("Categoria", "ComparacionVisual")]
public class ProyectosComparacionTests
{
    // Títulos que no entran en su columna: el mockup también los corta (v2-celda-cortada) y el píxel coincide.
    private static readonly string[] CortesDelMockup = ["Plan de mantenimiento vial del centro 2026"];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        string[]? recortesPermitidos = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos, aprobado: aprobado);

    // Aprobación de Martin (2026-10-30): antialias Chromium vs Skia en los tres montos y en los títulos "Clasificación",
    // "Tareas" y "Volver"; layout alineado. Tope = medido.
    private static readonly UmbralAprobado AprobacionSinFinanzas = new(16, "Martin", "2026-10-30",
        "Antialias Chromium vs Skia en montos y títulos de la ficha sin finanzas; layout alineado. Tope = medido.",
        "BotonGuardar sin Classes=primario (relleno equivocado): 32 teselas");

    private static Action<Control> SeleccionandoElProyecto(int id) => vista =>
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (ProyectoListViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.FilaSeleccionada = vm.Proyectos.Single(f => f.Id == id);
        Dispatcher.UIThread.RunJobs();
    };

    /// <summary>Scroll hasta la sección Gastos (su título queda a 24 del borde, el margen de la página) o hasta el final si la
    /// ficha no da para tanto.</summary>
    private static void OcultarSidebarYScrollearAGastos(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var ficha = vista.GetVisualDescendants().OfType<ProyectoFormView>().Single();
        var scroll = ficha.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Scroll");
        var gastos = ficha.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "SeccionGastos");
        var y = gastos.TranslatePoint(default, (Visual)scroll.Content!)!.Value.Y;
        scroll.Offset = new Vector(0, Math.Min(y, scroll.Extent.Height - scroll.Viewport.Height));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact] public void Proyectos_IgualAlMockup() =>
        Comparar(DatosProyectos.Listado(), "gestion/proyectos.html", preparar: SeleccionandoElProyecto(4), recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void Proyectos_IgualAlMockup_1366x768() =>
        Comparar(DatosProyectos.Listado(), "gestion/proyectos.html", TamanoLienzo.Municipio, SeleccionandoElProyecto(4), CortesDelMockup);
    [AvaloniaFact] public void ProyectoDetalleSobregiro_IgualAlMockup() =>
        Comparar(DatosProyectos.DetalleConSobregiro(), "gestion/proyecto-detalle.html", preparar: OcultarSidebarYScrollearAGastos);
    [AvaloniaFact] public void ProyectoDetalleSobregiro_IgualAlMockup_1366x768() =>
        Comparar(DatosProyectos.DetalleConSobregiro(), "gestion/proyecto-detalle.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAGastos);
    [AvaloniaFact] public void ProyectoDetalleSinFinanzas_IgualAlMockup() =>
        Comparar(DatosProyectos.DetalleSinFinanzas(), "gestion/proyecto-detalle-sin-finanzas.html", preparar: OcultarSidebarYScrollearAGastos,
            aprobado: AprobacionSinFinanzas);
    [AvaloniaFact] public void ProyectoDetalleSinFinanzas_IgualAlMockup_1366x768() =>
        Comparar(DatosProyectos.DetalleSinFinanzas(), "gestion/proyecto-detalle-sin-finanzas.html", TamanoLienzo.Municipio,
            OcultarSidebarYScrollearAGastos);
}
