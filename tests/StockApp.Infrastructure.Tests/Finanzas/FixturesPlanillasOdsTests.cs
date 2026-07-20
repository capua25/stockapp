using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

/// <summary>
/// Confirma que las 2 planillas reales del municipio (fixtures F5a) se copian al output
/// del proyecto de test. Si este test falla, revisar el &lt;None Include="Fixtures\Finanzas\*.ods"&gt;
/// de StockApp.Infrastructure.Tests.csproj.
/// </summary>
public class FixturesPlanillasOdsTests
{
    [Theory]
    [InlineData("PlanillaGastos2026.ods")]
    [InlineData("PlanillaPoa2026.ods")]
    public void Fixture_SeCopioAlOutput_ElArchivoExiste(string archivo)
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

        Assert.True(File.Exists(ruta), $"Falta el fixture '{archivo}' en {ruta}.");
    }
}
