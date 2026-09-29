using StockApp.Tests.Compartido;

namespace StockApp.Presentation.VisualTests;

/// <summary>
/// Review Focus 4: si VisualTestApp no carga lo mismo que App.axaml, la comparación visual
/// aprueba una app que no existe.
/// </summary>
public class IncludesAppAxamlTests
{
    [Fact]
    public void VisualTestApp_CargaLosMismosIncludesQueAppAxaml_EnElMismoOrden()
    {
        var appAxaml = File.ReadAllText(Path.Combine(RaizRepo.Ruta, "src", "StockApp.Presentation", "App.axaml"));

        var (recursos, estilos) = IncludesDeAppAxaml.Leer(appAxaml);

        Assert.Equal(recursos, VisualTestApp.ResourceIncludes);
        Assert.Equal(estilos, VisualTestApp.StyleIncludes);
    }
}
