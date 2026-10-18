using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Catálogos dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class CatalogosComparacionTests
{
    /// <summary>Proveedores: las columnas medidas ceden al ancho de la card y el texto largo se corta con elipsis y tooltip (aprobado
    /// en el GATE 1: la grilla no scrollea en horizontal). Son exactamente los valores que no entran en 1280 ni en 1366.</summary>
    private static readonly string[] CortesDeProveedores =
    [
        "ventas@barraca.uy", "compras@eltornillo.com.uy", "info@pinturaslitoral.uy", "18 de Julio 455", "Av. Artigas 1280",
        "Ruta 21 km 262, esquina camino a Playa Fomento, Carmelo, Colonia", "Entrega martes y jueves",
        "Cuenta corriente a 30 días, pedir factura con orden de compra",
    ];

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null,
        IReadOnlyCollection<string>? recortesPermitidos = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            recortesPermitidos: recortesPermitidos, ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void Catalogos_IgualAlMockup() => Comparar(DatosCatalogos.ConCategorias(), "administracion/catalogos.html");
    [AvaloniaFact] public void Catalogos_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.ConCategorias(), "administracion/catalogos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosProveedores_IgualAlMockup() => Comparar(DatosCatalogos.ConProveedores(), "administracion/catalogos-proveedores.html", recortesPermitidos: CortesDeProveedores);
    [AvaloniaFact] public void CatalogosProveedores_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.ConProveedores(), "administracion/catalogos-proveedores.html", TamanoLienzo.Municipio, recortesPermitidos: CortesDeProveedores);
    [AvaloniaFact] public void CatalogosLineasPoa_IgualAlMockup() => Comparar(DatosCatalogos.ConLineasPoa(), "administracion/catalogos-lineas-poa.html");
    [AvaloniaFact] public void CatalogosLineasPoa_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.ConLineasPoa(), "administracion/catalogos-lineas-poa.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosOperador_IgualAlMockup() => Comparar(DatosCatalogos.Operador(), "administracion/catalogos-operador.html");
    [AvaloniaFact] public void CatalogosOperador_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.Operador(), "administracion/catalogos-operador.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosCargando_IgualAlMockup() => Comparar(DatosCatalogos.Cargando(), "administracion/catalogos-cargando.html");
    [AvaloniaFact] public void CatalogosCargando_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.Cargando(), "administracion/catalogos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosSinConexion_IgualAlMockup() => Comparar(DatosCatalogos.SinConexion(), "administracion/catalogos-sin-conexion.html");
    [AvaloniaFact] public void CatalogosSinConexion_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.SinConexion(), "administracion/catalogos-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosSinPermiso_IgualAlMockup() => Comparar(DatosCatalogos.SinPermiso(), "administracion/catalogos-sin-permiso.html");
    [AvaloniaFact] public void CatalogosSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.SinPermiso(), "administracion/catalogos-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosVacio_IgualAlMockup() => Comparar(DatosCatalogos.Vacio(), "administracion/catalogos-vacio.html");
    [AvaloniaFact] public void CatalogosVacio_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.Vacio(), "administracion/catalogos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CatalogosSinResultados_IgualAlMockup() => Comparar(DatosCatalogos.SinResultados(), "administracion/catalogos-sin-resultados.html");
    [AvaloniaFact] public void CatalogosSinResultados_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.SinResultados(), "administracion/catalogos-sin-resultados.html", TamanoLienzo.Municipio);
}
