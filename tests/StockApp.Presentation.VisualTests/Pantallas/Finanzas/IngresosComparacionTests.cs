using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Ingresos de caja dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class IngresosComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    /// <summary>
    /// Sin resultados, a los dos tamaños: el título largo del estado vacío en negrita suma teselas de antialias Skia contra
    /// Chromium por encima del tope. Tope exacto, el medido.
    /// </summary>
    private static readonly UmbralAprobado AprobacionNegritasSinResultados = new(
        13,
        "Martin",
        "2026-10-12",
        "Antialias Skia vs Chromium en negritas: las 13 teselas densas con desplazamiento 0,0 (tono), sin geometría; 4 en el " +
        "título de la página y 9 en el título del estado vacío; tinta app/mockup con los mismos bordes y proporción 0,77-0,87; " +
        "0,265 % y 0,245 % de píxeles.");

    [AvaloniaFact] public void Ingresos_IgualAlMockup() => Comparar(DatosIngresos.ConIngresos(), "finanzas/ingresos.html");
    [AvaloniaFact] public void Ingresos_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConIngresos(), "finanzas/ingresos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosCargando_IgualAlMockup() => Comparar(DatosIngresos.Cargando(), "finanzas/ingresos-cargando.html");
    [AvaloniaFact] public void IngresosCargando_IgualAlMockup_1366x768() => Comparar(DatosIngresos.Cargando(), "finanzas/ingresos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosVacio_IgualAlMockup() => Comparar(DatosIngresos.SinDatos(), "finanzas/ingresos-vacio.html");
    [AvaloniaFact] public void IngresosVacio_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinDatos(), "finanzas/ingresos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosSinResultados_IgualAlMockup() => Comparar(DatosIngresos.SinResultados(), "finanzas/ingresos-sin-resultados.html", aprobado: AprobacionNegritasSinResultados);
    [AvaloniaFact] public void IngresosSinResultados_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinResultados(), "finanzas/ingresos-sin-resultados.html", TamanoLienzo.Municipio,
        aprobado: AprobacionNegritasSinResultados);
    [AvaloniaFact] public void IngresosSinConexion_IgualAlMockup() => Comparar(DatosIngresos.SinConexion(), "finanzas/ingresos-sin-conexion.html");
    [AvaloniaFact] public void IngresosSinConexion_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SinConexion(), "finanzas/ingresos-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosSoloLectura_IgualAlMockup() => Comparar(DatosIngresos.SoloLectura(), "finanzas/ingresos-solo-lectura.html");
    [AvaloniaFact] public void IngresosSoloLectura_IgualAlMockup_1366x768() => Comparar(DatosIngresos.SoloLectura(), "finanzas/ingresos-solo-lectura.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void IngresosConInactivos_IgualAlMockup() => Comparar(DatosIngresos.ConInactivos(), "finanzas/ingresos-con-inactivos.html");
    [AvaloniaFact] public void IngresosConInactivos_IgualAlMockup_1366x768() => Comparar(DatosIngresos.ConInactivos(), "finanzas/ingresos-con-inactivos.html", TamanoLienzo.Municipio);
}
