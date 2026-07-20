using StockApp.Infrastructure.Finanzas;
using Xunit;
using static StockApp.Infrastructure.Tests.Fixtures.Finanzas.OdsTestHelper;

namespace StockApp.Infrastructure.Tests.Finanzas;

public class PlanillaOdsParserGastosTests
{
    private const string FilaEncabezadoGastos = """
        <table:table-row>
          <table:table-cell office:value-type="string"><text:p>FECHA</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>FACTURA</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>ORDEN</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>PROVEEDOR</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>DESTINO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>GASTO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>INGRESO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>EGRESO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>SALDO</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>LITERAL</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>Código</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>RUBRO</text:p></table:table-cell>
        </table:table-row>
        """;

    private const string FilaEncabezadoVariables = """
        <table:table-row>
          <table:table-cell office:value-type="string"><text:p>LITERAL</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>Código</text:p></table:table-cell>
          <table:table-cell office:value-type="string"><text:p>RUBRO</text:p></table:table-cell>
        </table:table-row>
        """;

    private static (string Nombre, string FilasXml) MesVacio(string nombre) => (nombre, FilaEncabezadoGastos);

    [Fact]
    public void ParsearGastos_HojaConUnMovimiento_MapeaTodasLasColumnas()
    {
        var filaMovimiento = """
            <table:table-row>
              <table:table-cell office:value-type="date" office:date-value="2026-06-01"><text:p>01/06/26</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="20207"><text:p>20207</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="869785"><text:p>869785</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>COLORLUX</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>TEATRO DE VERANO</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>ARTÍCULOS DE LIMPIEZA</text:p></table:table-cell>
              <table:table-cell/>
              <table:table-cell office:value-type="float" office:value="29246"><text:p>29.246,00</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="526543"><text:p>526.543,00</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>B</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="14"><text:p>14</text:p></table:table-cell>
              <table:table-cell office:value-type="string" office:string-value="Teatro de Verano" table:formula="of:=VLOOKUP(1;1;1)"><text:p>Teatro de Verano</text:p></table:table-cell>
            </table:table-row>
            """;
        var filasVariables = FilaEncabezadoVariables + """
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>B</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="14"><text:p>14</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>Teatro de Verano</text:p></table:table-cell>
            </table:table-row>
            """;

        using var stream = CrearOdsFalso(
            MesVacio("ENERO"), MesVacio("FEBRERO"), MesVacio("MARZO"), MesVacio("ABRIL"), MesVacio("MAYO"),
            ("JUNIO", FilaEncabezadoGastos + filaMovimiento),
            MesVacio("JULIO"), MesVacio("AGOSTO"), MesVacio("SEPTIEMBRE"), MesVacio("OCTUBRE"),
            MesVacio("NOVIEMBRE"), MesVacio("DICIEMBRE"),
            ("Variables", filasVariables));

        var resultado = new PlanillaOdsParser().ParsearGastos(stream);

        var junio = Assert.Single(resultado.FilasPorMes["JUNIO"]);
        Assert.Equal(new DateOnly(2026, 6, 1), junio.Fecha);
        Assert.Equal("20207", junio.Factura);
        Assert.Equal("869785", junio.Orden);
        Assert.Equal("COLORLUX", junio.Proveedor);
        Assert.Equal("TEATRO DE VERANO", junio.Destino);
        Assert.Equal("ARTÍCULOS DE LIMPIEZA", junio.Gasto);
        Assert.Null(junio.Ingreso);
        Assert.Equal(29246m, junio.Egreso);
        Assert.Equal(526543m, junio.Saldo);
        Assert.Equal("B", junio.Literal);
        Assert.Equal(14, junio.Codigo);
        Assert.Equal("Teatro de Verano", junio.Rubro);
        Assert.Empty(resultado.FilasPorMes["ENERO"]);

        var variable = Assert.Single(resultado.Variables);
        Assert.Equal("B", variable.Literal);
        Assert.Equal(14, variable.Codigo);
        Assert.Equal("Teatro de Verano", variable.Rubro);
    }

    [Fact]
    public void ParsearGastos_FilaQueSoloArrastraSaldoSinMovimiento_SeOmite()
    {
        // Gotcha real (JUNIO de la planilla real, filas 46-200): solo tienen la fórmula de
        // SALDO copiada hacia abajo, sin FECHA/FACTURA/PROVEEDOR — no son movimientos.
        var filaSoloSaldo = """
            <table:table-row>
              <table:table-cell/><table:table-cell/><table:table-cell/><table:table-cell/>
              <table:table-cell/><table:table-cell/><table:table-cell/><table:table-cell/>
              <table:table-cell office:value-type="float" office:value="43705"><text:p>43.705,00</text:p></table:table-cell>
              <table:table-cell/><table:table-cell/><table:table-cell/>
            </table:table-row>
            """;

        using var stream = CrearOdsFalso(
            MesVacio("ENERO"), MesVacio("FEBRERO"), MesVacio("MARZO"), MesVacio("ABRIL"), MesVacio("MAYO"),
            ("JUNIO", FilaEncabezadoGastos + filaSoloSaldo),
            MesVacio("JULIO"), MesVacio("AGOSTO"), MesVacio("SEPTIEMBRE"), MesVacio("OCTUBRE"),
            MesVacio("NOVIEMBRE"), MesVacio("DICIEMBRE"),
            ("Variables", FilaEncabezadoVariables));

        var resultado = new PlanillaOdsParser().ParsearGastos(stream);

        Assert.Empty(resultado.FilasPorMes["JUNIO"]);
    }

    [Fact]
    public void ParsearGastos_FilaDeMovimientoSinFactura_ConColspanEnProveedorDestinoGasto_SeMapeaComoIngreso()
    {
        // Gotcha real (fila "SALDO ANTERIOR"/movimientos sin factura, ej. LIT. B, multas,
        // préstamos): PROVEEDOR+DESTINO+GASTO vienen fusionados (colspan=3) en una sola
        // celda de texto, FACTURA/ORDEN quedan vacíos.
        var filaSinFactura = """
            <table:table-row>
              <table:table-cell office:value-type="date" office:date-value="2026-06-01"><text:p>01/06/26</text:p></table:table-cell>
              <table:table-cell table:number-columns-repeated="2"/>
              <table:table-cell office:value-type="string" table:number-columns-spanned="3"><text:p>LIT. B </text:p></table:table-cell>
              <table:covered-table-cell table:number-columns-repeated="2"/>
              <table:table-cell office:value-type="float" office:value="300000"><text:p>300.000,00</text:p></table:table-cell>
              <table:table-cell/>
              <table:table-cell office:value-type="float" office:value="305789"><text:p>305.789,00</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>B</text:p></table:table-cell>
            </table:table-row>
            """;

        using var stream = CrearOdsFalso(
            MesVacio("ENERO"), MesVacio("FEBRERO"), MesVacio("MARZO"), MesVacio("ABRIL"), MesVacio("MAYO"),
            ("JUNIO", FilaEncabezadoGastos + filaSinFactura),
            MesVacio("JULIO"), MesVacio("AGOSTO"), MesVacio("SEPTIEMBRE"), MesVacio("OCTUBRE"),
            MesVacio("NOVIEMBRE"), MesVacio("DICIEMBRE"),
            ("Variables", FilaEncabezadoVariables));

        var resultado = new PlanillaOdsParser().ParsearGastos(stream);

        var fila = Assert.Single(resultado.FilasPorMes["JUNIO"]);
        Assert.Null(fila.Factura);
        Assert.Null(fila.Orden);
        Assert.Equal("LIT. B ", fila.Proveedor); // colspan: cae en la columna de PROVEEDOR
        Assert.Equal(300000m, fila.Ingreso);
        Assert.Equal(305789m, fila.Saldo);
    }
}
