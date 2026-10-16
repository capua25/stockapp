using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Formulario de gasto dentro del shell real (sidebar oculto en los dos renders) y los dos modales de Gastos.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class GastoFormComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    /// <summary>Contenido scrolleado hasta el final: se ve el pie fijo pegado al borde con la card terminada arriba.</summary>
    private static void OcultarSidebarYScrollearAlFinal(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var formulario = vista.GetVisualDescendants().OfType<GastoFormView>().Single();
        var desplazamiento = formulario.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Desplazamiento");
        desplazamiento.Offset = new Vector(0, desplazamiento.Extent.Height - desplazamiento.Viewport.Height);
        Dispatcher.UIThread.RunJobs();
    }

    // Aprobación puntual (no sube el umbral global) de Martin, 2026-10-16: antialias de Chromium contra Skia en párrafos de
    // varias líneas (los dos modales de Gastos) y en texto en negrita (edición a crédito a 1280). Sin corrimiento de layout:
    // los cortes de línea y las baselines coinciden con el mockup, y el porcentaje de píxeles queda en 0,37 a 0,44 %, bajo el
    // 1,68 % calibrado (no se afloja). Mismo mecanismo y forma que ConfirmacionPrecios. Tope = medido.
    private static UmbralAprobado AprobacionAntialias(int teselas, string motivo, string? mutacionPalida = null) => new(
        teselas, "Martin", "2026-10-16", motivo, mutacionPalida);

    private const string MutacionModal =
        "Border raíz de ConfirmacionDialog con V2FondoBrush en vez de V2SuperficieBrush (relleno pálido equivocado): 178 y 180 teselas en las cuatro corridas, contra 24 a 30 aprobadas";

    private static UmbralAprobado AprobacionModal(int teselas, string detalle) => AprobacionAntialias(
        teselas, $"Antialias Chromium vs Skia en el mensaje de varias líneas ({detalle}); sin corrimiento de layout, cortes de línea y baselines iguales; teselas solo sobre el texto. Tope = medido.", MutacionModal);

    private static Control Modal(string mensaje) => ContenidoDeDialogo.Extraer(new ConfirmacionDialog(mensaje));

    [AvaloniaFact] public void GastoNuevo_IgualAlMockup() => Comparar(DatosGastos.Nuevo(), "finanzas/gasto-nuevo.html");
    [AvaloniaFact] public void GastoNuevo_IgualAlMockup_1366x768() => Comparar(DatosGastos.Nuevo(), "finanzas/gasto-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoEditar_IgualAlMockup() =>
        Comparar(DatosGastos.Editar(), "finanzas/gasto-editar.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditar_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.Editar(), "finanzas/gasto-editar.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditarCredito_IgualAlMockup() =>
        Comparar(DatosGastos.EditarCredito(), "finanzas/gasto-editar-credito.html", preparar: OcultarSidebarYScrollearAlFinal,
            aprobado: AprobacionAntialias(13, "Antialias Chromium vs Skia en texto en negrita (títulos de sección, etiquetas y botones); sin corrimiento de layout; 0,41 % de píxeles. Tope = medido."));
    [AvaloniaFact] public void GastoEditarCredito_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.EditarCredito(), "finanzas/gasto-editar-credito.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoErrorCampo_IgualAlMockup() => Comparar(DatosGastos.ConErrorDeCampo(), "finanzas/gasto-error-campo.html");
    [AvaloniaFact] public void GastoErrorCampo_IgualAlMockup_1366x768() => Comparar(DatosGastos.ConErrorDeCampo(), "finanzas/gasto-error-campo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoErrorServidor_IgualAlMockup() => Comparar(DatosGastos.ConErrorDelServidor(), "finanzas/gasto-error-servidor.html");
    [AvaloniaFact] public void GastoErrorServidor_IgualAlMockup_1366x768() => Comparar(DatosGastos.ConErrorDelServidor(), "finanzas/gasto-error-servidor.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoCredito_IgualAlMockup() => Comparar(DatosGastos.Credito(), "finanzas/gasto-credito.html");
    [AvaloniaFact] public void GastoCredito_IgualAlMockup_1366x768() => Comparar(DatosGastos.Credito(), "finanzas/gasto-credito.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoSoloLectura_IgualAlMockup() => Comparar(DatosGastos.FormularioSoloLectura(), "finanzas/gasto-solo-lectura.html");
    [AvaloniaFact] public void GastoSoloLectura_IgualAlMockup_1366x768() => Comparar(DatosGastos.FormularioSoloLectura(), "finanzas/gasto-solo-lectura.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoAnulado_IgualAlMockup() => Comparar(DatosGastos.FormularioAnulado(), "finanzas/gasto-anulado.html");
    [AvaloniaFact] public void GastoAnulado_IgualAlMockup_1366x768() => Comparar(DatosGastos.FormularioAnulado(), "finanzas/gasto-anulado.html", TamanoLienzo.Municipio);

    [AvaloniaFact] public void GastoConfirmarAnular_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeConfirmarAnular()), null, "finanzas/gasto-confirmar-anular.html",
            aprobado: AprobacionModal(30, "0,44 % de píxeles"));
    [AvaloniaFact] public void GastoConfirmarAnular_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeConfirmarAnular()), null, "finanzas/gasto-confirmar-anular.html",
            tamano: TamanoLienzo.Municipio, aprobado: AprobacionModal(28, "0,41 % de píxeles"));
    [AvaloniaFact] public void GastoFacturaDuplicada_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeFacturaDuplicada()), null, "finanzas/gasto-factura-duplicada.html",
            aprobado: AprobacionModal(24, "0,40 % de píxeles"));
    [AvaloniaFact] public void GastoFacturaDuplicada_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeFacturaDuplicada()), null, "finanzas/gasto-factura-duplicada.html",
            tamano: TamanoLienzo.Municipio, aprobado: AprobacionModal(28, "0,37 % de píxeles"));
}
