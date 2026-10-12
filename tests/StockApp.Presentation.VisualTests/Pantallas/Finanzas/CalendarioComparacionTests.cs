using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Calendario de pagos dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class CalendarioComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void Calendario_IgualAlMockup() => Comparar(DatosCalendario.ConDatos(), "finanzas/calendario-pagos.html");
    [AvaloniaFact] public void Calendario_IgualAlMockup_1366x768() => Comparar(DatosCalendario.ConDatos(), "finanzas/calendario-pagos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CalendarioCargando_IgualAlMockup() => Comparar(DatosCalendario.Cargando(), "finanzas/calendario-pagos-cargando.html");
    [AvaloniaFact] public void CalendarioCargando_IgualAlMockup_1366x768() => Comparar(DatosCalendario.Cargando(), "finanzas/calendario-pagos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CalendarioVacio_IgualAlMockup() => Comparar(DatosCalendario.SinDatos(), "finanzas/calendario-pagos-vacio.html");
    [AvaloniaFact] public void CalendarioVacio_IgualAlMockup_1366x768() => Comparar(DatosCalendario.SinDatos(), "finanzas/calendario-pagos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CalendarioSinConexion_IgualAlMockup() => Comparar(DatosCalendario.SinConexion(), "finanzas/calendario-pagos-sin-conexion.html");
    [AvaloniaFact] public void CalendarioSinConexion_IgualAlMockup_1366x768() => Comparar(DatosCalendario.SinConexion(), "finanzas/calendario-pagos-sin-conexion.html", TamanoLienzo.Municipio);
}
