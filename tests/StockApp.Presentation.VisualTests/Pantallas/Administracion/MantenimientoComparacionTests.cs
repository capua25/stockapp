using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Mantenimiento, dentro del shell real con el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class MantenimientoComparacionTests
{
    /// <summary>La nota de la corrida reconciliada se corta con elipsis y tooltip cuando no entra (a 1280).</summary>
    private static readonly string[] CorteDelMockup = ["Esta fila no proviene de una corrida real: se dio de alta al reconciliar un dump existente."];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null,
        IReadOnlyCollection<string>? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado, recortesPermitidos: recortesPermitidos,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void Mantenimiento_IgualAlMockup() => Comparar(DatosMantenimiento.ConCorridas(), "administracion/mantenimiento.html", recortesPermitidos: CorteDelMockup);
    [AvaloniaFact] public void Mantenimiento_IgualAlMockup_1366x768() => Comparar(DatosMantenimiento.ConCorridas(), "administracion/mantenimiento.html", TamanoLienzo.Municipio, recortesPermitidos: CorteDelMockup);
    [AvaloniaFact] public void MantenimientoVacio_IgualAlMockup() => Comparar(DatosMantenimiento.Vacio(), "administracion/mantenimiento-vacio.html");
    [AvaloniaFact] public void MantenimientoVacio_IgualAlMockup_1366x768() => Comparar(DatosMantenimiento.Vacio(), "administracion/mantenimiento-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void MantenimientoError_IgualAlMockup() => Comparar(DatosMantenimiento.ConErrores(), "administracion/mantenimiento-error.html");
    [AvaloniaFact] public void MantenimientoError_IgualAlMockup_1366x768() => Comparar(DatosMantenimiento.ConErrores(), "administracion/mantenimiento-error.html", TamanoLienzo.Municipio);
}
