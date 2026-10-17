using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Tareas;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>Formulario de Tareas (alta y detalle) dentro del shell real y el diálogo de reclasificar (área cliente).</summary>
[Trait("Categoria", "ComparacionVisual")]
public class TareaFormComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    /// <summary>Contenido scrolleado hasta el final: se ve el pie fijo pegado al borde con la card terminada arriba.</summary>
    private static void OcultarSidebarYScrollearAlFinal(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var formulario = vista.GetVisualDescendants().OfType<TareaFormView>().Single();
        var scroll = formulario.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Scroll");
        scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>El área cliente se saca del Window y pierde su DataContext: se le pasa el mismo ViewModel a la comparación.</summary>
    private static void CompararDialogo(TamanoLienzo? tamano = null)
    {
        var vm = DatosTareas.Reclasificar();
        ComparacionVisual.CompararConMockup(ContenidoDeDialogo.Extraer(new ReclasificarTareaDialog { DataContext = vm }), vm,
            "gestion/reclasificar-tarea.html", tamano: tamano);
    }

    [AvaloniaFact] public void TareaNueva_IgualAlMockup() =>
        Comparar(DatosTareas.Nueva(), "gestion/tarea-nueva.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaNueva_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.Nueva(), "gestion/tarea-nueva.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaNuevaErrorCampo_IgualAlMockup() =>
        Comparar(DatosTareas.NuevaConErrorDeCampo(), "gestion/tarea-nueva-error-campo.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaNuevaErrorCampo_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.NuevaConErrorDeCampo(), "gestion/tarea-nueva-error-campo.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaDetalle_IgualAlMockup() =>
        Comparar(DatosTareas.Detalle(), "gestion/tarea-detalle.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaDetalle_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.Detalle(), "gestion/tarea-detalle.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaDetalleOperador_IgualAlMockup() =>
        Comparar(DatosTareas.DetalleOperador(), "gestion/tarea-detalle-operador.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void TareaDetalleOperador_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.DetalleOperador(), "gestion/tarea-detalle-operador.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void ReclasificarTarea_IgualAlMockup() =>
        CompararDialogo();
    [AvaloniaFact] public void ReclasificarTarea_IgualAlMockup_1366x768() =>
        CompararDialogo(TamanoLienzo.Municipio);
}
