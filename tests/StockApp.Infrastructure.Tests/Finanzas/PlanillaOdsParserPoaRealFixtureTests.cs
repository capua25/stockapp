using StockApp.Infrastructure.Finanzas;
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

/// <summary>
/// Red de seguridad de integración: parsea la planilla POA REAL del municipio
/// (PlanillaPoa2026.ods, copiada al output vía CopyToOutputDirectory) para blindar el corte de
/// lectura de cada hoja de línea. Este test es el que FALTÓ y dejó pasar dos veces el bug de la
/// fila de TOTAL fantasma: los fixtures sintéticos son fáciles de "acomodar" para dar verde, la
/// planilla real no. Si este test se pone rojo, la lógica de corte volvió a romperse.
/// </summary>
public class PlanillaOdsParserPoaRealFixtureTests
{
    private static PlanillaOdsParser Parser => new();

    private static Stream AbrirPlanillaReal()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", "PlanillaPoa2026.ods");
        Assert.True(File.Exists(ruta), $"Falta el fixture real 'PlanillaPoa2026.ods' en {ruta}.");
        return File.OpenRead(ruta);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_LeeLosSaldosConsolidados()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        Assert.Equal(6643349m, resultado.SaldosTotales.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosTotales.SaldoLiteralC);
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_HerramientasTieneUnSoloMovimientoSinContarElTotal()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var herramientas = resultado.Lineas.Single(l => l.Hoja == "HERRAMIENTAS");
        var movimiento = Assert.Single(herramientas.Movimientos);
        Assert.Equal(140000m, movimiento.Importe);
        // El importe 140000 es a la vez el del ÚNICO movimiento y el de la fila de TOTAL del
        // fondo. Si el total colara como movimiento fantasma, habría 2 filas con 140000.
        Assert.Equal(1, herramientas.Movimientos.Count(m => m.Importe == 140000m));
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_CarpetaAsfalticaTieneUnMovimientoSinFacturaYSinTotalDuplicado()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var carpeta = resultado.Lineas.Single(l => l.Hoja == "CARPETA ASFÁLTICA");
        var movimiento = Assert.Single(carpeta.Movimientos);
        Assert.Null(movimiento.Factura);
        Assert.Equal(5500000m, movimiento.Importe);
        // 5500000 aparece como movimiento (sin factura, arriba del hueco) Y como fila de TOTAL.
        // Debe contarse UNA sola vez.
        Assert.Equal(1, carpeta.Movimientos.Count(m => m.Importe == 5500000m));
    }

    [Fact]
    public void ParsearPoa_PlanillaReal_MejorasEnBarraconesTieneSieteMovimientosSinContarElTotal()
    {
        using var stream = AbrirPlanillaReal();

        var resultado = Parser.ParsearPoa(stream);

        var barracones = resultado.Lineas.Single(l => l.Hoja == "MEJORAS EN BARRACONES");
        Assert.Equal(7, barracones.Movimientos.Count);
        // La suma de los 7 movimientos reales coincide con la fila de TOTAL (62329); esa fila NO
        // debe aparecer como un octavo movimiento con importe 62329.
        Assert.Equal(62329m, barracones.Movimientos.Sum(m => m.Importe ?? 0m));
        Assert.DoesNotContain(barracones.Movimientos, m => m.Importe == 62329m);
    }
}
