using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Control POA dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class ControlPoaComparacionTests
{
    // A 1280 la columna LÍNEA mide 224 y este nombre no entra: el mockup también lo corta (elipsis y tooltip).
    private static readonly string[] CorteDelMockupA1280 = ["Mantenimiento de espacios públicos"];

    // El único cambio entre la fila normal y la sobreejecutada es la barra de 64 x 6 que pasa a peligro: con el tope
    // por defecto (11 teselas) la diferencia cabe sobrada. Con 2 (la comparación mide 1) el tono de la barra se custodia.
    private const int TeselasConLaBarraCustodiada = 2;

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, string[]? recortesPermitidos = null,
        int teselasDensasMax = UmbralComparacion.TeselasDensasPorDefecto) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, teselasDensasMax: teselasDensasMax,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    [AvaloniaFact] public void ControlPoa_IgualAlMockup() => Comparar(DatosControlPoa.ConLineas(), "finanzas/control-poa.html", recortesPermitidos: CorteDelMockupA1280);
    [AvaloniaFact] public void ControlPoa_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.ConLineas(), "finanzas/control-poa.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaSobreejecutado_IgualAlMockup() => Comparar(DatosControlPoa.Sobreejecutado(), "finanzas/control-poa-sobreejecutado.html", recortesPermitidos: CorteDelMockupA1280, teselasDensasMax: TeselasConLaBarraCustodiada);
    [AvaloniaFact] public void ControlPoaSobreejecutado_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.Sobreejecutado(), "finanzas/control-poa-sobreejecutado.html", TamanoLienzo.Municipio, teselasDensasMax: TeselasConLaBarraCustodiada);
    [AvaloniaFact] public void ControlPoaCargando_IgualAlMockup() => Comparar(DatosControlPoa.Cargando(), "finanzas/control-poa-cargando.html");
    [AvaloniaFact] public void ControlPoaCargando_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.Cargando(), "finanzas/control-poa-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaVacio_IgualAlMockup() => Comparar(DatosControlPoa.SinLineas(), "finanzas/control-poa-vacio.html");
    [AvaloniaFact] public void ControlPoaVacio_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.SinLineas(), "finanzas/control-poa-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaSinConexion_IgualAlMockup() => Comparar(DatosControlPoa.SinConexion(), "finanzas/control-poa-sin-conexion.html");
    [AvaloniaFact] public void ControlPoaSinConexion_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.SinConexion(), "finanzas/control-poa-sin-conexion.html", TamanoLienzo.Municipio);
}
