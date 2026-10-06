using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Dialogos;

[Trait("Categoria", "ComparacionVisual")]
public class DialogosComparacionTests
{
    private static Control Confirmacion() =>
        ContenidoDeDialogo.Extraer(new ConfirmacionDialog("¿Cerrar la sesión?"));

    private static Control Mensaje() =>
        ContenidoDeDialogo.Extraer(new MensajeDialog("No se puede dar de baja la categoría: tiene productos asociados."));

    private static Control PedirTexto() =>
        ContenidoDeDialogo.Extraer(new PedirTextoDialog("Anular documento", "Ingresá el motivo de la anulación:"));

    [AvaloniaFact]
    public void Confirmacion_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Confirmacion(), null, "dialogos/confirmacion.html");

    [AvaloniaFact]
    public void Confirmacion_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Confirmacion(), null, "dialogos/confirmacion.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void Mensaje_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Mensaje(), null, "dialogos/mensaje.html");

    [AvaloniaFact]
    public void Mensaje_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Mensaje(), null, "dialogos/mensaje.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void PedirTexto_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(PedirTexto(), null, "dialogos/pedir-texto.html");

    [AvaloniaFact]
    public void PedirTexto_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(PedirTexto(), null, "dialogos/pedir-texto.html", tamano: TamanoLienzo.Municipio);
}
