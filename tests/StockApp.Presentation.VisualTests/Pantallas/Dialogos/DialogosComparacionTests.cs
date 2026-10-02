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

    // Párrafo de 2 líneas a 14px: 12 teselas densas a 1280x760 (1366x768 da 11 y pasa con lo calibrado). Medido por
    // filas y columnas de tinta: la geometría coincide con el mockup al píxel; la diferencia es el tono del rasterizador
    // (el texto de Skia sale más fino que el de Chromium, diferencia aceptada en spec §6.5). Aprobado por el usuario.
    private static readonly UmbralAprobado MensajeA1280 = new(
        12, "Martin", "2026-10-02",
        "tono del rasterizador (Skia vs Chromium) en un párrafo de 2 líneas, no geometría; filas y columnas de tinta idénticas");

    [AvaloniaFact]
    public void Confirmacion_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Confirmacion(), null, "dialogos/confirmacion.html");

    [AvaloniaFact]
    public void Confirmacion_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Confirmacion(), null, "dialogos/confirmacion.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void Mensaje_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Mensaje(), null, "dialogos/mensaje.html", aprobado: MensajeA1280);

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
