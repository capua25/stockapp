using System.IO;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// TestApp replica a mano los includes de App.axaml. Si divergen, TODOS los tests de UI miden
/// una app que no existe: un estilo v2 incluido en producción y olvidado acá pasa en verde.
/// </summary>
public class IncludesAppAxamlTests
{
    [Fact]
    public void TestApp_CargaLosMismosIncludesQueAppAxaml_EnElMismoOrden()
    {
        var appAxaml = File.ReadAllText(Path.Combine(RaizRepo.Ruta, "src", "StockApp.Presentation", "App.axaml"));

        var (recursos, estilos) = IncludesDeAppAxaml.Leer(appAxaml);

        Assert.Equal(recursos, TestApp.ResourceIncludes);
        Assert.Equal(estilos, TestApp.StyleIncludes);
    }
}
