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

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, string[]? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    [AvaloniaFact] public void LibroCaja_IgualAlMockup() => Comparar(DatosLibroCaja.ConMovimientos(), "finanzas/libro-caja.html", recortesPermitidos: CorteDelMockup);
    [AvaloniaFact] public void LibroCaja_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.ConMovimientos(), "finanzas/libro-caja.html", TamanoLienzo.Municipio, CorteDelMockup);
    [AvaloniaFact] public void LibroCajaSaldoNegativo_IgualAlMockup() => Comparar(DatosLibroCaja.SaldoNegativo(), "finanzas/libro-caja-saldo-negativo.html", recortesPermitidos: CorteDelMockupSaldoNegativo);
    [AvaloniaFact] public void LibroCajaSaldoNegativo_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SaldoNegativo(), "finanzas/libro-caja-saldo-negativo.html", TamanoLienzo.Municipio, CorteDelMockupSaldoNegativo);
    [AvaloniaFact] public void LibroCajaVacio_IgualAlMockup() => Comparar(DatosLibroCaja.SinMovimientos(), "finanzas/libro-caja-vacio.html");
    [AvaloniaFact] public void LibroCajaVacio_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinMovimientos(), "finanzas/libro-caja-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaCargando_IgualAlMockup() => Comparar(DatosLibroCaja.Cargando(), "finanzas/libro-caja-cargando.html");
    [AvaloniaFact] public void LibroCajaCargando_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.Cargando(), "finanzas/libro-caja-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSinConexion_IgualAlMockup() => Comparar(DatosLibroCaja.SinConexion(), "finanzas/libro-caja-sin-conexion.html");
    [AvaloniaFact] public void LibroCajaSinConexion_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinConexion(), "finanzas/libro-caja-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LibroCajaSinPermiso_IgualAlMockup() => Comparar(DatosLibroCaja.SinPermiso(), "finanzas/libro-caja-sin-permiso.html");
    [AvaloniaFact] public void LibroCajaSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosLibroCaja.SinPermiso(), "finanzas/libro-caja-sin-permiso.html", TamanoLienzo.Municipio);
}
