using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Registrar Entrada / Salida (spec §5.2) dentro del shell real, con la vista resuelta por ViewLocator y el
/// sidebar oculto en los dos renders. El estado se aplica en prepararApp, después de que InicializarAsync
/// cargó los productos (la carga vacía la lista y el combo suelta la selección).
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class MovimientosComparacionTests
{
    private static readonly string[] OcultarEnMockup = [".v2-sidebar"];

    private static void Comparar(DatosMovimientos.Estado estado, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null)
    {
        var (shell, vm) = DatosMovimientos.EnShell(estado);
        ComparacionVisual.CompararConMockup(new ShellMainView(), shell, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: OcultarEnMockup,
            prepararApp: vista =>
            {
                ProductosComparacionTests.OcultarSidebar(vista);
                DatosMovimientos.Aplicar(vm, estado);
            });
    }

    [AvaloniaFact]
    public void EntradaCompra_IgualAlMockup() => Comparar(DatosMovimientos.EntradaCompra, "inventario/entrada-compra.html");

    [AvaloniaFact]
    public void EntradaCompra_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.EntradaCompra, "inventario/entrada-compra.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void EntradaAjuste_IgualAlMockup() => Comparar(DatosMovimientos.EntradaAjuste, "inventario/entrada-ajuste.html");

    [AvaloniaFact]
    public void EntradaAjuste_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.EntradaAjuste, "inventario/entrada-ajuste.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void EntradaError_IgualAlMockup() => Comparar(DatosMovimientos.EntradaError, "inventario/entrada-error.html");

    [AvaloniaFact]
    public void EntradaError_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.EntradaError, "inventario/entrada-error.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void SalidaUso_IgualAlMockup() => Comparar(DatosMovimientos.SalidaUso, "inventario/salida-uso.html");

    [AvaloniaFact]
    public void SalidaUso_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.SalidaUso, "inventario/salida-uso.html", TamanoLienzo.Municipio);
}
