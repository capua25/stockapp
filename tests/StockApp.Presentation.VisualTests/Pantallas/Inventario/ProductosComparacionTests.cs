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
    public void ProductosInactivos_IgualAlMockup() => Comparar(DatosProductos.ConProductosInactivos(), "inventario/productos-inactivos.html");

    [AvaloniaFact]
    public void ProductosInactivos_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.ConProductosInactivos(), "inventario/productos-inactivos.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductosVacio_IgualAlMockup() => Comparar(DatosProductos.SinProductos(), "inventario/productos-vacio.html");

    [AvaloniaFact]
    public void ProductosVacio_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinProductos(), "inventario/productos-vacio.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductosCargando_IgualAlMockup() => Comparar(DatosProductos.Cargando(), "inventario/productos-cargando.html");

    [AvaloniaFact]
    public void ProductosCargando_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.Cargando(), "inventario/productos-cargando.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductosSinPermiso_IgualAlMockup() => Comparar(DatosProductos.SinPermisoListado(), "inventario/productos-sin-permiso.html");

    [AvaloniaFact]
    public void ProductosSinPermiso_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinPermisoListado(), "inventario/productos-sin-permiso.html", TamanoLienzo.Municipio);

    // Cortes a propósito, con elipsis y tooltip, que el mockup también corta: la unidad larga (UNIDAD cede al espacio
    // libre) y los nombres que no entran en NOMBRE, que con UNIDAD cediendo queda en su piso de 160.
    private static readonly string[] CortesDelMockup =
    [
        DatosProductos.UnidadLarga, "Cemento portland 50 kg", "Pintura látex blanca 20 L", "Hipoclorito de sodio 5 L",
        "Bolsas de residuo 80x110",
    ];

    [AvaloniaFact]
    public void ProductosCeldaCortada_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosProductos.ConCeldaCortada(), "inventario/productos-celda-cortada.html",
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar, recortesPermitidos: CortesDelMockup);

    [AvaloniaFact]
    public void ProductosCeldaCortada_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosProductos.ConCeldaCortada(), "inventario/productos-celda-cortada.html",
            tamano: TamanoLienzo.Municipio, ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar, recortesPermitidos: CortesDelMockup);

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
