using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Cinco de los diez formularios de catálogo dentro del shell real (los otros cinco tienen la misma estructura y los
/// custodia FormulariosCatalogoV2Tests), con el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class FormulariosCatalogoComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void CategoriaNueva_IgualAlMockup() => Comparar(DatosCatalogos.CategoriaNueva(), "administracion/categoria-nueva.html");
    [AvaloniaFact] public void CategoriaNueva_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.CategoriaNueva(), "administracion/categoria-nueva.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CategoriaError_IgualAlMockup() => Comparar(DatosCatalogos.CategoriaError(), "administracion/categoria-error.html");
    [AvaloniaFact] public void CategoriaError_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.CategoriaError(), "administracion/categoria-error.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ProveedorEditar_IgualAlMockup() => Comparar(DatosCatalogos.ProveedorEditar(), "administracion/proveedor-editar.html");
    [AvaloniaFact] public void ProveedorEditar_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.ProveedorEditar(), "administracion/proveedor-editar.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void RubroNuevo_IgualAlMockup() => Comparar(DatosCatalogos.RubroNuevo(), "administracion/rubro-nuevo.html");
    [AvaloniaFact] public void RubroNuevo_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.RubroNuevo(), "administracion/rubro-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void LineaPoaEditar_IgualAlMockup() => Comparar(DatosCatalogos.LineaPoaEditar(), "administracion/linea-poa-editar.html");
    [AvaloniaFact] public void LineaPoaEditar_IgualAlMockup_1366x768() => Comparar(DatosCatalogos.LineaPoaEditar(), "administracion/linea-poa-editar.html", TamanoLienzo.Municipio);
}
