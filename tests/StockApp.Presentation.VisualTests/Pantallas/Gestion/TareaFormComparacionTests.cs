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
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    // Aprobación puntual de Martin (2026-10-17), no sube el umbral global: antialias de Chromium contra Skia en texto de cuerpo
    // y en negrita. Verificado en cada pantalla: ninguna tesela fuera del texto, ninguna línea con desvío mayor a 1 px, línea base
    // igual y sin deriva entre líneas, sin teselas desplazadas salvo el texto de "Volver" (1 px: el texto de Skia es más angosto).
    // Tope = medido.
    private static UmbralAprobado AprobacionAntialias(int teselas, string? mutacionPalida = null) => new(
        teselas, "Martin", "2026-10-17",
        "Antialias Chromium vs Skia en texto de cuerpo y en negrita; layout alineado (cajas de texto a 1 px, misma línea base), teselas solo sobre el texto. Tope = medido.",
        mutacionPalida);

    // Aprobación de Martin (2026-10-30): con la tarea sin clasificadores (spec Proyectos) el detalle es 52 px más corto y el
    // scroll deja a la vista texto que antes quedaba arriba; las teselas nuevas son antialias de texto, sin desvío de layout.
    private static UmbralAprobado AprobacionProyecto(int teselas, string mutacionPalida) => new(
        teselas, "Martin", "2026-10-30",
        "Antialias Chromium vs Skia en el texto que queda a la vista tras quitar los clasificadores; layout alineado. Tope = medido.",
        mutacionPalida);

    private const string MutacionAgregarNota = "BotonAgregarNota sin Classes=secundario (relleno equivocado): ";


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
    private static void CompararDialogo(UmbralAprobado aprobado, TamanoLienzo? tamano = null)
    {
        var vm = DatosTareas.Reclasificar();
        ComparacionVisual.CompararConMockup(ContenidoDeDialogo.Extraer(new ReclasificarTareaDialog { DataContext = vm }), vm,
            "gestion/reclasificar-tarea.html", tamano: tamano, aprobado: aprobado);
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
        Comparar(DatosTareas.Detalle(), "gestion/tarea-detalle.html", preparar: OcultarSidebarYScrollearAlFinal,
            aprobado: AprobacionAntialias(32, MutacionAgregarNota + "162 teselas"));
    [AvaloniaFact] public void TareaDetalle_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.Detalle(), "gestion/tarea-detalle.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal,
            AprobacionAntialias(17, MutacionAgregarNota + "115 teselas"));
    [AvaloniaFact] public void TareaDetalleOperador_IgualAlMockup() =>
        Comparar(DatosTareas.DetalleOperador(), "gestion/tarea-detalle-operador.html", preparar: OcultarSidebarYScrollearAlFinal,
            aprobado: AprobacionAntialias(42, MutacionAgregarNota + "165 teselas"));
    [AvaloniaFact] public void TareaDetalleOperador_IgualAlMockup_1366x768() =>
        Comparar(DatosTareas.DetalleOperador(), "gestion/tarea-detalle-operador.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal,
            AprobacionProyecto(22, MutacionAgregarNota + "125 teselas"));
    [AvaloniaFact] public void ReclasificarTarea_IgualAlMockup() =>
        CompararDialogo(AprobacionAntialias(13));
    [AvaloniaFact] public void ReclasificarTarea_IgualAlMockup_1366x768() =>
        CompararDialogo(AprobacionAntialias(13), TamanoLienzo.Municipio);
}
