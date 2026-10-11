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
        string[] ocultar = estado.Guardado ? [".v2-sidebar", ".panel-adjuntos-v1"] : [".v2-sidebar"];
        ComparacionVisual.CompararConMockup(new ShellMainView(), shell, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: ocultar,
            prepararApp: vista =>
            {
                ProductosComparacionTests.OcultarSidebar(vista);
                DatosIngresoFactura.Aplicar(vm, estado);
                if (estado.Guardado)
                {
                    // La card de adjuntos recién se hizo visible: su ContentControl resuelve AdjuntosPanelView en el
                    // próximo layout.
                    vista.UpdateLayout();
                    DatosIngresoFactura.OcultarPanelAdjuntosV1(vista);
                }
            });
    }

    /// <summary>
    /// A 1280 el texto en negrita (título de la página, subtítulos de sección y valores del pie) suma teselas de antialias
    /// Skia contra Chromium por encima del tope. Tope exacto por pantalla, el medido.
    /// </summary>
    private static UmbralAprobado AprobacionNegritas(int teselas) => new(
        teselas,
        "controlador SDD (decisiones delegadas por el usuario el 2026-10-11)",
        "2026-10-11",
        "Antialias Skia vs Chromium en negritas a 1280: todas las teselas densas con desplazamiento 0,0 (tono), en el título, " +
        "\"Datos de la factura\", \"Cargar artículo\" y los valores del pie; tinta app/mockup estable 0,80-0,94 con los mismos " +
        "bordes; 0,69 % y 0,70 % de píxeles. A 1366 las mismas pantallas quedan en 9.");

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
    public void IngresoFacturaGuardado_IgualAlMockup() => Comparar(DatosIngresoFactura.Guardado, "inventario/ingreso-factura-guardado.html");

    [AvaloniaFact]
    public void IngresoFacturaGuardado_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.Guardado, "inventario/ingreso-factura-guardado.html", TamanoLienzo.Municipio);
}
