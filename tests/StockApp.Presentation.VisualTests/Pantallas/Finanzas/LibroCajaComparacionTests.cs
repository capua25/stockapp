using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Libro caja dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class LibroCajaComparacionTests
{
    // CONCEPTO queda en su piso de 200 a 1280 y a 1366 (la grilla scrollea en horizontal): estos conceptos no entran y el
    // mockup aprobado también los corta (elipsis y tooltip).
    private static readonly string[] CorteDelMockup =
        ["Materiales para bacheo de calle Zorrilla", "Partida mensual FIGM octubre", "Combustible de la flota municipal"];

    private static readonly string[] CorteDelMockupSaldoNegativo =
        ["Materiales para bacheo de calle Zorrilla", "Adelanto de la partida FIGM octubre"];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, string[]? recortesPermitidos = null,
        UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    private const string MotivoNegritasVacio =
        "Antialias Skia vs Chromium en negritas: todas las teselas densas con desplazamiento 0,0 (tono), sin geometría; 2 en el " +
        "título de la página, 6 en el título del estado vacío y el resto en los saldos; centroides app/mockup a menos de 0,1 px. " +
        "Mismo caso que IngresosSinResultados de la 4b (13 teselas). 0,297 % y 0,275 % de píxeles.";

    /// <summary>Mes vacío a 1280: el título del estado vacío y los saldos en negrita suman teselas de tono por encima del tope.</summary>
    private static readonly UmbralAprobado AprobacionVacio1280 = new(13, "Martin", "2026-10-14", MotivoNegritasVacio);

    /// <summary>Mes vacío a 1366: las mismas teselas de tono, una menos.</summary>
    private static readonly UmbralAprobado AprobacionVacio1366 = new(12, "Martin", "2026-10-14", MotivoNegritasVacio);

    /// <summary>
    /// Saldo negativo, a los dos tamaños: la Insignia "Saldo negativo" (12 px) renderiza 1 a 1,5 px más angosta en Avalonia
    /// que en el navegador, así que lo que queda a su izquierda (inicio de la insignia y el bloque Saldo inicial) se corre 1 px
    /// a la derecha; no visible. El resto, tono de negritas.
    /// </summary>
    private static readonly UmbralAprobado AprobacionSaldoNegativo = new(
        16,
        "Martin",
        "2026-10-14",
        "Corrimiento de 1 px (dx=-1 en 12 a 13 teselas) porque la Insignia \"Saldo negativo\" renderiza 1 a 1,5 px más angosta en " +
        "Avalonia que en el navegador; no visible. El resto, tono de negritas (desplazamiento 0,0). 0,709 % y 0,673 % de píxeles.",
        "Sin Classes.negativo en el saldo final (número pálido): 21 teselas a 1280 y 20 a 1366; sin la Insignia del saldo " +
        "final: 20 y 20. Las dos en rojo contra el tope de 16.");

    [AvaloniaFact] public void LibroCaja_IgualAlMockup() => Comparar(DatosLibroCaja.ConMovimientos(), "finanzas/libro-caja.html", recortesPermitidos: CorteDelMockup);
    [AvaloniaFact] public void LibroCaja_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.ConMovimientos(), "finanzas/libro-caja.html", TamanoLienzo.Municipio, CorteDelMockup);
    [AvaloniaFact] public void LibroCajaSaldoNegativo_IgualAlMockup() => Comparar(DatosLibroCaja.SaldoNegativo(), "finanzas/libro-caja-saldo-negativo.html", recortesPermitidos: CorteDelMockupSaldoNegativo, aprobado: AprobacionSaldoNegativo);
    [AvaloniaFact] public void LibroCajaSaldoNegativo_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SaldoNegativo(), "finanzas/libro-caja-saldo-negativo.html", TamanoLienzo.Municipio, CorteDelMockupSaldoNegativo,
        AprobacionSaldoNegativo);
    [AvaloniaFact] public void LibroCajaVacio_IgualAlMockup() => Comparar(DatosLibroCaja.SinMovimientos(), "finanzas/libro-caja-vacio.html", aprobado: AprobacionVacio1280);
    [AvaloniaFact] public void LibroCajaVacio_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinMovimientos(), "finanzas/libro-caja-vacio.html", TamanoLienzo.Municipio,
        aprobado: AprobacionVacio1366);
    [AvaloniaFact] public void LibroCajaCargando_IgualAlMockup() => Comparar(DatosLibroCaja.Cargando(), "finanzas/libro-caja-cargando.html");
    [AvaloniaFact] public void LibroCajaCargando_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.Cargando(), "finanzas/libro-caja-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSinConexion_IgualAlMockup() => Comparar(DatosLibroCaja.SinConexion(), "finanzas/libro-caja-sin-conexion.html");
    [AvaloniaFact] public void LibroCajaSinConexion_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinConexion(), "finanzas/libro-caja-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSinPermiso_IgualAlMockup() => Comparar(DatosLibroCaja.SinPermiso(), "finanzas/libro-caja-sin-permiso.html");
    [AvaloniaFact] public void LibroCajaSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinPermiso(), "finanzas/libro-caja-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaAnio_IgualAlMockup() => Comparar(DatosLibroCaja.AnioCompleto(), "finanzas/libro-caja-anio.html");
    [AvaloniaFact] public void LibroCajaAnio_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.AnioCompleto(), "finanzas/libro-caja-anio.html", TamanoLienzo.Municipio);
}
