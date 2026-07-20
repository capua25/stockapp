using StockApp.Infrastructure.Finanzas;
using Xunit;
using static StockApp.Infrastructure.Tests.Fixtures.Finanzas.OdsTestHelper;

namespace StockApp.Infrastructure.Tests.Finanzas;

public class PlanillaOdsParserPoaTests
{
    // Bloque PRESUPUESTO/SALDO/LITERAL (colspan=2 en cada valor, como en la planilla real).
    private const string BloquePresupuestoLiteralB = """
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>PRESUPUESTO</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>SALDO</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        <table:table-row>
          <table:table-cell office:value-type="float" office:value="500000" table:number-columns-spanned="2"><text:p>500.000,00</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="float" office:value="360000" table:number-columns-spanned="2"><text:p>360.000,00</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>LITERAL B</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    // Igual que BloquePresupuestoLiteralB pero con PRESUPUESTO/SALDO/LITERAL parametrizables —
    // usado para probar líneas con LITERAL C y valores distintos de PRESUPUESTO/SALDO.
    private static string BloquePresupuestoLiteral(string literal, decimal presupuesto, decimal saldo) => $"""
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>PRESUPUESTO</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>SALDO</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        <table:table-row>
          <table:table-cell office:value-type="float" office:value="{presupuesto}" table:number-columns-spanned="2"><text:p>{presupuesto}</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="float" office:value="{saldo}" table:number-columns-spanned="2"><text:p>{saldo}</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>LITERAL {literal}</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    // Encabezado de datos: FACTURA(2) ORDEN(2) PROVEEDOR(2) GASTO(4) IMPORTE(2) — todo colspan.
    private const string EncabezadoDatosPoa = """
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>FACTURA</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>ORDEN</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>PROVEEDOR</text:p></table:table-cell>
          <table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>GASTO</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>IMPORTE</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    private const string FilaMovimientoCepillo = """
        <table:table-row>
          <table:table-cell table:number-columns-spanned="2"/><table:covered-table-cell/>
          <table:table-cell table:number-columns-spanned="2"/><table:covered-table-cell/>
          <table:table-cell table:number-columns-spanned="2"/><table:covered-table-cell/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>cepillo</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="float" office:value="140000" table:number-columns-spanned="2"><text:p>140.000,00</text:p></table:table-cell>
          <table:covered-table-cell/>
        </table:table-row>
        """;

    // "SALDO TOTALES": etiqueta (rowspan 2) → 1 fila separadora → valor (rowspan 2), misma
    // columna — mismo layout verificado contra la planilla real.
    private static string HojaSaldoTotales(int saldoB, int saldoC) => $"""
        <table:table-row>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>SALDO LITERAL B</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>SALDO LITERAL C</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
        </table:table-row>
        <table:table-row><table:table-cell table:number-columns-repeated="8"/></table:table-row>
        <table:table-row><table:table-cell table:number-columns-repeated="8"/></table:table-row>
        <table:table-row>
          <table:table-cell office:value-type="float" office:value="{saldoB}" table:number-columns-spanned="4"><text:p>{saldoB}</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
          <table:table-cell office:value-type="float" office:value="{saldoC}" table:number-columns-spanned="4"><text:p>{saldoC}</text:p></table:table-cell>
          <table:covered-table-cell table:number-columns-repeated="3"/>
        </table:table-row>
        """;

    [Fact]
    public void ParsearPoa_UnaLineaConPresupuestoYUnMovimiento_MapeaCorrectamente()
    {
        var filasLinea = BloquePresupuestoLiteralB + EncabezadoDatosPoa + FilaMovimientoCepillo;

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLinea),
            ("SALDO TOTALES", HojaSaldoTotales(6643349, 4654206)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal("LINEA1", linea.Hoja);
        Assert.Equal(500000m, linea.Presupuesto);
        Assert.Equal(360000m, linea.Saldo);
        Assert.Equal("B", linea.Literal);

        var movimiento = Assert.Single(linea.Movimientos);
        Assert.Null(movimiento.Factura);
        Assert.Null(movimiento.Orden);
        Assert.Null(movimiento.Proveedor);
        Assert.Equal("cepillo", movimiento.Gasto);
        Assert.Equal(140000m, movimiento.Importe);

        Assert.Equal(6643349m, resultado.SaldosTotales.SaldoLiteralB);
        Assert.Equal(4654206m, resultado.SaldosTotales.SaldoLiteralC);
    }

    [Fact]
    public void ParsearPoa_ExcluyeLaHojaSaldoTotalesDeLaListaDeLineas()
    {
        var filasLineaSinMovimientos = BloquePresupuestoLiteralB + EncabezadoDatosPoa;

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLineaSinMovimientos),
            ("SALDO TOTALES", HojaSaldoTotales(1, 1)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal("LINEA1", linea.Hoja);
        Assert.Empty(linea.Movimientos);
    }

    // Bug CRÍTICO detectado en review: cada hoja de línea real termina con una fila de TOTAL
    // (solo columna IMPORTE, con la suma de la línea) separada de los movimientos por un HUECO
    // de filas vacías. El código viejo la colaba como un FilaPoaOds fantasma porque solo
    // descartaba filas con las 5 columnas TODAS null. El fix corta la lectura en la primera fila
    // totalmente vacía tras el encabezado de datos.

    [Fact]
    public void ParsearPoa_MovimientosRealesConHuecoYFilaTotalAlFondo_ExcluyeLaFilaTotal()
    {
        var filasLinea = BloquePresupuestoLiteralB + EncabezadoDatosPoa
            + FilaPoa(gasto: "cepillo", importe: 140000m)
            + FilaPoa(gasto: "pintura", importe: 20000m)
            + FilaPoaVacia() // hueco
            + FilaPoa(importe: 160000m); // fila TOTAL fantasma (= suma de los 2 movimientos reales)

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLinea),
            ("SALDO TOTALES", HojaSaldoTotales(1, 1)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal(2, linea.Movimientos.Count);
        Assert.Equal("cepillo", linea.Movimientos[0].Gasto);
        Assert.Equal(140000m, linea.Movimientos[0].Importe);
        Assert.Equal("pintura", linea.Movimientos[1].Gasto);
        Assert.Equal(20000m, linea.Movimientos[1].Importe);
        Assert.DoesNotContain(linea.Movimientos, m => m.Importe == 160000m);
    }

    [Fact]
    public void ParsearPoa_HojaSinMovimientosConFilaTotalCeroAlFondo_NoAgregaMovimientoFantasma()
    {
        var filasLinea = BloquePresupuestoLiteralB + EncabezadoDatosPoa
            + FilaPoaVacia() // hueco: la línea nunca tuvo movimientos
            + FilaPoa(importe: 0m); // fila TOTAL fantasma (suma de una línea vacía = 0)

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLinea),
            ("SALDO TOTALES", HojaSaldoTotales(1, 1)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Empty(linea.Movimientos);
    }

    [Fact]
    public void ParsearPoa_MovimientoConImporteSinFacturaArribaDelHueco_SeIncluye()
    {
        // Caso real verificado: CARPETA ASFÁLTICA tiene un movimiento legítimo con importe
        // pero SIN factura, ARRIBA del hueco. NO hay que confundirlo con la fila total de abajo.
        var filasLinea = BloquePresupuestoLiteralB + EncabezadoDatosPoa
            + FilaPoa(importe: 5500000m) // movimiento real, sin factura
            + FilaPoaVacia() // hueco
            + FilaPoa(importe: 5500000m); // fila TOTAL fantasma (= suma de un único movimiento)

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLinea),
            ("SALDO TOTALES", HojaSaldoTotales(1, 1)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        var linea = Assert.Single(resultado.Lineas);
        var movimiento = Assert.Single(linea.Movimientos);
        Assert.Null(movimiento.Factura);
        Assert.Equal(5500000m, movimiento.Importe);
    }

    [Fact]
    public void ParsearPoa_MultiplesHojasDeLineaConLiteralC_ParseaCadaUnaExcluyendoSuFilaTotal()
    {
        var filasLinea1 = BloquePresupuestoLiteralB + EncabezadoDatosPoa
            + FilaPoa(gasto: "cepillo", importe: 140000m)
            + FilaPoaVacia()
            + FilaPoa(importe: 140000m);

        var filasLinea2 = BloquePresupuestoLiteral("C", 300000m, 250000m) + EncabezadoDatosPoa
            + FilaPoa(gasto: "pintura", importe: 20000m)
            + FilaPoa(gasto: "cal", importe: 30000m)
            + FilaPoaVacia()
            + FilaPoa(importe: 50000m);

        using var stream = CrearOdsFalso(
            ("LINEA1", filasLinea1),
            ("LINEA2", filasLinea2),
            ("SALDO TOTALES", HojaSaldoTotales(1, 1)));

        var resultado = new PlanillaOdsParser().ParsearPoa(stream);

        Assert.Equal(2, resultado.Lineas.Count);

        var linea1 = resultado.Lineas.Single(l => l.Hoja == "LINEA1");
        Assert.Equal("B", linea1.Literal);
        var movimiento1 = Assert.Single(linea1.Movimientos);
        Assert.Equal(140000m, movimiento1.Importe);

        var linea2 = resultado.Lineas.Single(l => l.Hoja == "LINEA2");
        Assert.Equal("C", linea2.Literal);
        Assert.Equal(300000m, linea2.Presupuesto);
        Assert.Equal(250000m, linea2.Saldo);
        Assert.Equal(2, linea2.Movimientos.Count);
        Assert.DoesNotContain(linea2.Movimientos, m => m.Importe == 50000m);
    }
}
