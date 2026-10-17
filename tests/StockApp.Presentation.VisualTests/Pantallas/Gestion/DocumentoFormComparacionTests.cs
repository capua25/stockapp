using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Documentos;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Gestion;

/// <summary>Formulario de Documentos (alta y detalle) dentro del shell real, con el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class DocumentoFormComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    /// <summary>Contenido scrolleado hasta el final: se ve el pie fijo pegado al borde con la card terminada arriba.</summary>
    private static void OcultarSidebarYScrollearAlFinal(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var formulario = vista.GetVisualDescendants().OfType<DocumentoFormView>().Single();
        var scroll = formulario.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Scroll");
        scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact] public void DocumentoNuevo_IgualAlMockup() => Comparar(DatosDocumentos.Nuevo(), "gestion/documento-nuevo.html");
    [AvaloniaFact] public void DocumentoNuevo_IgualAlMockup_1366x768() => Comparar(DatosDocumentos.Nuevo(), "gestion/documento-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void DocumentoErrorCampo_IgualAlMockup() => Comparar(DatosDocumentos.NuevoConErrorDeCampo(), "gestion/documento-error-campo.html");
    [AvaloniaFact] public void DocumentoErrorCampo_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.NuevoConErrorDeCampo(), "gestion/documento-error-campo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void DocumentoDetalle_IgualAlMockup() =>
        Comparar(DatosDocumentos.Detalle(), "gestion/documento-detalle.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void DocumentoDetalle_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.Detalle(), "gestion/documento-detalle.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void DocumentoDetalleCerrado_IgualAlMockup() =>
        Comparar(DatosDocumentos.DetalleCerrado(), "gestion/documento-detalle-cerrado.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void DocumentoDetalleCerrado_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.DetalleCerrado(), "gestion/documento-detalle-cerrado.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void DocumentoDetalleOperador_IgualAlMockup() =>
        Comparar(DatosDocumentos.DetalleOperador(), "gestion/documento-detalle-operador.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void DocumentoDetalleOperador_IgualAlMockup_1366x768() =>
        Comparar(DatosDocumentos.DetalleOperador(), "gestion/documento-detalle-operador.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
}
