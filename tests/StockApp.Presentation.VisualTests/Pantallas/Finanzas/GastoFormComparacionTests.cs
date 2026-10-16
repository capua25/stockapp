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
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
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

    private static Control Modal(string mensaje) => ContenidoDeDialogo.Extraer(new ConfirmacionDialog(mensaje));

    [AvaloniaFact] public void GastoNuevo_IgualAlMockup() => Comparar(DatosGastos.Nuevo(), "finanzas/gasto-nuevo.html");
    [AvaloniaFact] public void GastoNuevo_IgualAlMockup_1366x768() => Comparar(DatosGastos.Nuevo(), "finanzas/gasto-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoEditar_IgualAlMockup() =>
        Comparar(DatosGastos.Editar(), "finanzas/gasto-editar.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditar_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.Editar(), "finanzas/gasto-editar.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditarCredito_IgualAlMockup() =>
        Comparar(DatosGastos.EditarCredito(), "finanzas/gasto-editar-credito.html", preparar: OcultarSidebarYScrollearAlFinal);
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
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeConfirmarAnular()), null, "finanzas/gasto-confirmar-anular.html");
    [AvaloniaFact] public void GastoConfirmarAnular_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeConfirmarAnular()), null, "finanzas/gasto-confirmar-anular.html",
            tamano: TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoFacturaDuplicada_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeFacturaDuplicada()), null, "finanzas/gasto-factura-duplicada.html");
    [AvaloniaFact] public void GastoFacturaDuplicada_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeFacturaDuplicada()), null, "finanzas/gasto-factura-duplicada.html",
            tamano: TamanoLienzo.Municipio);
}
