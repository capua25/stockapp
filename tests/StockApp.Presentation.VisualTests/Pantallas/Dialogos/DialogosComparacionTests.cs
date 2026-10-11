using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
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

    private static Control RecalcularStock() =>
        ContenidoDeDialogo.Extraer(new RecalcularStockDialog((_, _) => Task.FromResult<IEnumerable<object>>(Array.Empty<object>())));

    private static Control AltaProducto() =>
        ContenidoDeDialogo.Extraer(new AltaProductoRapidaDialog(DatosProductos.Categorias, DatosProductos.Unidades));

    private static Control AltaProductoFaltantes()
    {
        var dialogo = new AltaProductoRapidaDialog(DatosProductos.Categorias, DatosProductos.Unidades);
        dialogo.FindControl<TextBox>("CodigoTextBox")!.Text = "MAT-0601";
        dialogo.FindControl<Button>("ConfirmarButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return ContenidoDeDialogo.Extraer(dialogo);
    }

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

    [AvaloniaFact]
    public void RecalcularStock_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(RecalcularStock(), null, "dialogos/recalcular-stock.html");

    [AvaloniaFact]
    public void RecalcularStock_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(RecalcularStock(), null, "dialogos/recalcular-stock.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AltaProducto_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(AltaProducto(), null, "dialogos/alta-producto.html");

    [AvaloniaFact]
    public void AltaProducto_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(AltaProducto(), null, "dialogos/alta-producto.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AltaProductoFaltantes_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(AltaProductoFaltantes(), null, "dialogos/alta-producto-faltantes.html");

    [AvaloniaFact]
    public void AltaProductoFaltantes_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(AltaProductoFaltantes(), null, "dialogos/alta-producto-faltantes.html", tamano: TamanoLienzo.Municipio);
}
