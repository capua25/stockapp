using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Gastos y facturas dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class GastosComparacionTests
{
    // DETALLE (*) queda en su piso de 200 en los dos tamaños (la grilla de doce columnas hace scroll horizontal) y estos
    // detalles no entran: el mockup también los corta (v2-celda-cortada, col-detalle de 200 px) y el píxel coincide.
    private static readonly string[] CortesDelMockup =
    [
        "Reparación de cordón cuneta en calle Zorrilla", "Combustible de la flota municipal (duplicado)",
        "Combustible de la flota municipal", "Materiales para bacheo de calle Zorrilla",
        "Arena y pedregullo para caminería rural", "Cableado del alumbrado de la rambla",
    ];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null,
        string[]? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar,
            recortesPermitidos: recortesPermitidos);

    /// <summary>La fila anulada seleccionada (Anular deshabilitado). Se selecciona con la vista ya cargada: la carga que dispara
    /// la vista reemplaza las filas.</summary>
    private static void OcultarSidebarYSeleccionarLaAnulada(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (GastosViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.FilaSeleccionada = vm.Filas.Single(f => !f.Gasto.Activo);
    }

    [AvaloniaFact] public void Gastos_IgualAlMockup() => Comparar(DatosGastos.ConGastos(), "finanzas/gastos.html", recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void Gastos_IgualAlMockup_1366x768() => Comparar(DatosGastos.ConGastos(), "finanzas/gastos.html", TamanoLienzo.Municipio, recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void GastosCargando_IgualAlMockup() => Comparar(DatosGastos.Cargando(), "finanzas/gastos-cargando.html");
    [AvaloniaFact] public void GastosCargando_IgualAlMockup_1366x768() => Comparar(DatosGastos.Cargando(), "finanzas/gastos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSinConexion_IgualAlMockup() => Comparar(DatosGastos.SinConexion(), "finanzas/gastos-sin-conexion.html");
    [AvaloniaFact] public void GastosSinConexion_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinConexion(), "finanzas/gastos-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSinPermiso_IgualAlMockup() => Comparar(DatosGastos.SinPermiso(), "finanzas/gastos-sin-permiso.html");
    [AvaloniaFact] public void GastosSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinPermiso(), "finanzas/gastos-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosVacio_IgualAlMockup() => Comparar(DatosGastos.SinGastos(), "finanzas/gastos-vacio.html");
    [AvaloniaFact] public void GastosVacio_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinGastos(), "finanzas/gastos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSinResultados_IgualAlMockup() => Comparar(DatosGastos.SinResultados(), "finanzas/gastos-sin-resultados.html");
    [AvaloniaFact] public void GastosSinResultados_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinResultados(), "finanzas/gastos-sin-resultados.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSoloLectura_IgualAlMockup() => Comparar(DatosGastos.SoloLectura(), "finanzas/gastos-solo-lectura.html", recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void GastosSoloLectura_IgualAlMockup_1366x768() => Comparar(DatosGastos.SoloLectura(), "finanzas/gastos-solo-lectura.html", TamanoLienzo.Municipio, recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void GastosConAnulado_IgualAlMockup() =>
        Comparar(DatosGastos.ConAnulado(), "finanzas/gastos-con-anulado.html", preparar: OcultarSidebarYSeleccionarLaAnulada, recortesPermitidos: CortesDelMockup);
    [AvaloniaFact] public void GastosConAnulado_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.ConAnulado(), "finanzas/gastos-con-anulado.html", TamanoLienzo.Municipio, OcultarSidebarYSeleccionarLaAnulada, CortesDelMockup);
}
