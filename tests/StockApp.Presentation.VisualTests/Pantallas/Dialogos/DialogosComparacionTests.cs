using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using StockApp.Presentation.ViewModels.Movimientos;
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

    private static Control ConfirmacionPrecios() =>
        ContenidoDeDialogo.Extraer(new ConfirmacionPreciosDialog(
        [
            new ItemConfirmacionPrecioVm { Fila = new FilaRenglonFacturaVm(), ProductoNombre = "Cemento portland 50 kg", PrecioActual = 480m, PrecioNuevo = 510m, Confirmado = true },
            new ItemConfirmacionPrecioVm { Fila = new FilaRenglonFacturaVm(), ProductoNombre = "Arena gruesa", PrecioActual = 890m, PrecioNuevo = 920m },
            new ItemConfirmacionPrecioVm { Fila = new FilaRenglonFacturaVm(), ProductoNombre = "Cable 2x1,5 mm", PrecioActual = 38m, PrecioNuevo = 41.5m },
        ]));

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

    // Aprobación puntual (no sube el umbral global): el mensaje de dos líneas largas de 14 px da teselas por antialias
    // Skia vs Chromium. Medido: 12 a 1280 y 15 a 1366, desplazamiento 0,0, misma tinta, ninguna en precios, tildes,
    // encabezados ni botones. Tope = medido.
    private static UmbralAprobado AprobacionConfirmacionPrecios(int teselas) => new(
        teselas,
        "controlador SDD (decisiones delegadas por el usuario el 2026-10-11)",
        "2026-10-11",
        "Antialias Skia vs Chromium en el mensaje de dos líneas; desplazamiento 0,0, tinta del mismo color, sin teselas en precios, tildes, encabezados ni botones; 0,39 % y 0,36 % de píxeles. Tope = medido.",
        null);

    [AvaloniaFact]
    public void ConfirmacionPrecios_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(ConfirmacionPrecios(), null, "dialogos/confirmacion-precios.html",
            aprobado: AprobacionConfirmacionPrecios(12));

    [AvaloniaFact]
    public void ConfirmacionPrecios_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(ConfirmacionPrecios(), null, "dialogos/confirmacion-precios.html", tamano: TamanoLienzo.Municipio,
            aprobado: AprobacionConfirmacionPrecios(15));
}
