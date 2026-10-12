using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Ingreso por factura (spec §5.2) dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en
/// los dos renders. El estado se aplica en prepararApp, después de la carga de los combos.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class IngresoFacturaComparacionTests
{
    private static void Comparar(DatosIngresoFactura.Estado estado, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null)
    {
        var (shell, vm) = DatosIngresoFactura.EnShell();
        ComparacionVisual.CompararConMockup(new ShellMainView(), shell, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"],
            prepararApp: vista =>
            {
                ProductosComparacionTests.OcultarSidebar(vista);
                DatosIngresoFactura.Aplicar(vm, estado);
                if (estado.Guardado)
                {
                    // La card de adjuntos recién se hizo visible: su ContentControl resuelve AdjuntosPanelView en el
                    // próximo layout.
                    vista.UpdateLayout();
                }
            });
    }

    /// <summary>
    /// A 1280 el texto en negrita (título de la página, subtítulos de sección y valores del pie) suma teselas de antialias
    /// Skia contra Chromium por encima del tope. Tope exacto por pantalla, el medido.
    /// </summary>
    private static UmbralAprobado AprobacionNegritas(int teselas) => new(
        teselas,
        "Martin",
        "2026-10-11",
        "Antialias Skia vs Chromium en negritas a 1280: todas las teselas densas con desplazamiento 0,0 (tono), en el título, " +
        "\"Datos de la factura\", \"Cargar artículo\" y los valores del pie; tinta app/mockup estable 0,80-0,94 con los mismos " +
        "bordes; 0,69 % y 0,70 % de píxeles. A 1366 las mismas pantallas quedan en 9.");

    /// <summary>Ingreso por factura guardado a 1280: 19 teselas, todas tono (desplazamiento 0,0). Además de las negritas de
    /// las otras pantallas, el nombre del adjunto. A 1366 queda en 3 con el umbral por defecto.</summary>
    private static readonly UmbralAprobado AprobacionGuardado = new(
        19,
        "Martin",
        "2026-10-11",
        "Diferencia de tono de fuente Skia vs navegador en negritas del título, los subtítulos, el nombre del adjunto y el pie; " +
        "0 teselas de geometría (corrimiento 0,0 en las 19) y el panel de adjuntos alineado con el mockup.",
        "Tarjeta sin su relleno V2Superficie (queda el fondo de la app, #F7F7F5 contra #FFFFFF): 1160 teselas a 1280 y 1250 a 1366");

    [AvaloniaFact]
    public void IngresoFactura_IgualAlMockup() => Comparar(DatosIngresoFactura.Vacio, "inventario/ingreso-factura.html");

    [AvaloniaFact]
    public void IngresoFactura_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.Vacio, "inventario/ingreso-factura.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void IngresoFacturaRenglones_IgualAlMockup() =>
        Comparar(DatosIngresoFactura.ConRenglones, "inventario/ingreso-factura-renglones.html", aprobado: AprobacionNegritas(13));

    [AvaloniaFact]
    public void IngresoFacturaRenglones_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.ConRenglones, "inventario/ingreso-factura-renglones.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void IngresoFacturaDiferencia_IgualAlMockup() =>
        Comparar(DatosIngresoFactura.ConDiferencia, "inventario/ingreso-factura-diferencia.html", aprobado: AprobacionNegritas(12));

    [AvaloniaFact]
    public void IngresoFacturaDiferencia_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.ConDiferencia, "inventario/ingreso-factura-diferencia.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void IngresoFacturaGuardado_IgualAlMockup() => Comparar(DatosIngresoFactura.Guardado, "inventario/ingreso-factura-guardado.html", aprobado: AprobacionGuardado);

    [AvaloniaFact]
    public void IngresoFacturaGuardado_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.Guardado, "inventario/ingreso-factura-guardado.html", TamanoLienzo.Municipio);
}
