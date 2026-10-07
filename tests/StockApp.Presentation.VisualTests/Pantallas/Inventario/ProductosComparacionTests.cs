using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Productos (spec §5.1 y §5.2) dentro del shell real, con la vista resuelta por ViewLocator. El sidebar
/// tiene su propia comparación (ShellComparacionTests): se oculta en los dos renders sin mover el layout.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class ProductosComparacionTests
{
    private static readonly string[] OcultarEnMockup = [".v2-sidebar"];

    public static void OcultarSidebar(Control vista) =>
        ((ShellMainView)vista).FindControl<Border>("Sidebar")!.IsVisible = false;

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar);

    [AvaloniaFact]
    public void Productos_IgualAlMockup() => Comparar(DatosProductos.ConProductos(), "inventario/productos.html");

    [AvaloniaFact]
    public void Productos_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.ConProductos(), "inventario/productos.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductosVacio_IgualAlMockup() => Comparar(DatosProductos.SinProductos(), "inventario/productos-vacio.html");

    [AvaloniaFact]
    public void ProductosVacio_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinProductos(), "inventario/productos-vacio.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoNuevo_IgualAlMockup() => Comparar(DatosProductos.Nuevo(), "inventario/producto-nuevo.html");

    [AvaloniaFact]
    public void ProductoNuevo_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.Nuevo(), "inventario/producto-nuevo.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoEditar_IgualAlMockup() => Comparar(DatosProductos.Editar(), "inventario/producto-editar.html");

    [AvaloniaFact]
    public void ProductoEditar_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.Editar(), "inventario/producto-editar.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoError_IgualAlMockup() => Comparar(DatosProductos.ConError(), "inventario/producto-error.html");

    [AvaloniaFact]
    public void ProductoError_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.ConError(), "inventario/producto-error.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoSinPermiso_IgualAlMockup() => Comparar(DatosProductos.SinPermiso(), "inventario/producto-sin-permiso.html");

    [AvaloniaFact]
    public void ProductoSinPermiso_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinPermiso(), "inventario/producto-sin-permiso.html", TamanoLienzo.Municipio);
}
