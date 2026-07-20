using StockApp.Infrastructure.Finanzas;
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

/// <summary>
/// Test de aceptación F5a (spec §11, criterio duro): parseando las DOS planillas reales del
/// municipio (fixtures en Fixtures/Finanzas/), los saldos cacheados deben coincidir EXACTO
/// con los 3 valores verificados manualmente contra las planillas: caja a junio 2026 =
/// 43.705; POA Literal B = 6.643.349; POA Literal C = 4.654.206.
/// </summary>
public class PlanillaOdsParserAceptacionTests
{
    private static string RutaFixture(string archivo) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

    [Fact]
    public void ParsearGastos_PlanillaReal_SaldoDeJunioEs43705()
    {
        using var stream = File.OpenRead(RutaFixture("PlanillaGastos2026.ods"));
        var parser = new PlanillaOdsParser();

        var resultado = parser.ParsearGastos(stream);

        var filasJunio = resultado.FilasPorMes["JUNIO"];
        Assert.NotEmpty(filasJunio);
        // El último movimiento del mes ya trae cacheado el saldo final (las filas
        // posteriores solo arrastran el mismo valor sin cambiarlo, y se descartan por no
        // ser movimientos — ver Task 3).
        Assert.Equal(43705m, filasJunio[^1].Saldo);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_SaldosLiteralByCCoincidenConLaPlanilla()
    {
        using var stream = File.OpenRead(RutaFixture("PlanillaPoa2026.ods"));
        var parser = new PlanillaOdsParser();

        var resultado = parser.ParsearPoa(stream);

        Assert.Equal(6643349m, resultado.SaldosTotales.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosTotales.SaldoLiteralC);
    }
}
