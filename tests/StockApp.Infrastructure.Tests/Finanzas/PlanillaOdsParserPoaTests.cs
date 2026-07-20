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
}
