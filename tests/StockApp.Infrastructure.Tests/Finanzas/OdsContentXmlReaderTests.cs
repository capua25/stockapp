using System.Xml.Linq;
using StockApp.Infrastructure.Finanzas;
using Xunit;

namespace StockApp.Infrastructure.Tests.Finanzas;

public class OdsContentXmlReaderTests
{
    private static XDocument Documento(string filasXml, string nombreHoja = "Hoja1") => XDocument.Parse($"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
          <office:body>
            <office:spreadsheet>
              <table:table table:name="{nombreHoja}">
                {filasXml}
              </table:table>
            </office:spreadsheet>
          </office:body>
        </office:document-content>
        """);

    [Fact]
    public void LeerHoja_CeldaTextoYFloat_LeeValoresDirectos()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>PROVEEDOR</text:p></table:table-cell>
              <table:table-cell office:value-type="float" office:value="1234.5"><text:p>1.234,50</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("PROVEEDOR", hoja.Celda(0, 0).Texto);
        Assert.Equal(1234.5m, hoja.Celda(0, 1).Numero);
    }

    [Fact]
    public void LeerHoja_ColumnasRepetidas_AvanzaElIndiceDeColumna()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell table:number-columns-repeated="3"/>
              <table:table-cell office:value-type="string"><text:p>DESPUES_DE_3_VACIAS</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.True(hoja.Celda(0, 0).EsVacia);
        Assert.True(hoja.Celda(0, 2).EsVacia);
        Assert.Equal("DESPUES_DE_3_VACIAS", hoja.Celda(0, 3).Texto);
    }

    [Fact]
    public void LeerHoja_ColspanConCoveredCell_NoDesalineaLaSiguienteCeldaReal()
    {
        // Gotcha más grave (POA: ~1.631 celdas con colspan=2 en columnas de datos).
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" table:number-columns-spanned="2"><text:p>FACTURA</text:p></table:table-cell>
              <table:covered-table-cell/>
              <table:table-cell office:value-type="string"><text:p>ORDEN</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("FACTURA", hoja.Celda(0, 0).Texto);
        Assert.True(hoja.Celda(0, 1).EsVacia);
        Assert.Equal("ORDEN", hoja.Celda(0, 2).Texto);
    }

    [Fact]
    public void LeerHoja_ColspanConCoveredCellRepetido_AvanzaElIndiceElNumeroCorrectoDeColumnas()
    {
        // covered-table-cell puede venir con table:number-columns-repeated (varias columnas
        // cubiertas comprimidas en un solo elemento XML) — colspan=4 real.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" table:number-columns-spanned="4"><text:p>GASTO</text:p></table:table-cell>
              <table:covered-table-cell table:number-columns-repeated="3"/>
              <table:table-cell office:value-type="string"><text:p>IMPORTE</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("GASTO", hoja.Celda(0, 0).Texto);
        Assert.Equal("IMPORTE", hoja.Celda(0, 4).Texto);
    }

    [Fact]
    public void LeerHoja_FilaConNumberRowsRepeatedGrande_CortaLaLecturaSinMaterializar()
    {
        // Gotcha: LibreOffice declara hasta 1.048.576 filas por hoja; solo unas pocas tienen
        // datos, el resto es UNA fila con number-rows-repeated masivo (valores reales vistos:
        // 1048375, 1048521). Eso SÍ hay que cortarlo — es el único caso que justifica el break,
        // no cualquier number-rows-repeated>1 (ver test de bloque chico más abajo).
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>FILA REAL</text:p></table:table-cell>
            </table:table-row>
            <table:table-row table:number-rows-repeated="1048575">
              <table:table-cell/>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal(1, hoja.CantidadFilas);
        Assert.Equal("FILA REAL", hoja.Celda(0, 0).Texto);
        Assert.True(hoja.Celda(1, 0).EsVacia);
    }

    [Fact]
    public void LeerHoja_FilaConNumberRowsRepeatedChicoAlInicio_NoCortaYSigueLeyendoDatosReales()
    {
        // Bug real detectado en PlanillaGastos2026.ods (hojas ANUAL y GRAFICAS): el patrón es
        // [number-rows-repeated="3" de filas vacías AL INICIO][~44 filas de datos reales: tabla
        // "TOTALES POR RUBRO"][relleno final con number-rows-repeated≈1048521]. Con el break
        // viejo (cualquier number-rows-repeated>1 corta) se perdía TODA la tabla de totales.
        var doc = Documento("""
            <table:table-row table:number-rows-repeated="3">
              <table:table-cell/>
            </table:table-row>
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>TOTALES POR RUBRO</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal(4, hoja.CantidadFilas);
        Assert.True(hoja.Celda(0, 0).EsVacia);
        Assert.True(hoja.Celda(1, 0).EsVacia);
        Assert.True(hoja.Celda(2, 0).EsVacia);
        Assert.Equal("TOTALES POR RUBRO", hoja.Celda(3, 0).Texto);
    }

    [Fact]
    public void LeerHoja_FilaConNumberRowsRepeatedChicoConContenido_ReplicaElContenidoYAlineaLasFilasSiguientes()
    {
        // Caso improbable pero cubierto: si una fila con number-rows-repeated CHICO trajera
        // contenido, ese contenido debe replicarse en cada una de las filas que representa
        // (no solo en la primera), y el índice de fila debe seguir alineado para lo que sigue.
        var doc = Documento("""
            <table:table-row table:number-rows-repeated="2">
              <table:table-cell office:value-type="string"><text:p>REPETIDA</text:p></table:table-cell>
            </table:table-row>
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>SIGUIENTE</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal(3, hoja.CantidadFilas);
        Assert.Equal("REPETIDA", hoja.Celda(0, 0).Texto);
        Assert.Equal("REPETIDA", hoja.Celda(1, 0).Texto);
        Assert.Equal("SIGUIENTE", hoja.Celda(2, 0).Texto);
    }

    [Fact]
    public void LeerHoja_CeldaFecha_LeeOfficeDateValue()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="date" office:date-value="2026-06-01"><text:p>01/06/26</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal(new DateOnly(2026, 6, 1), hoja.Celda(0, 0).Fecha);
    }

    [Fact]
    public void LeerHoja_CeldaStringConFormula_PrefiereOfficeStringValueSobreTextoPlano()
    {
        // Gotcha descubierto en la planilla real: RUBRO se calcula con VLOOKUP; el cache de
        // una fórmula de texto vive en office:string-value, no alcanza con leer <text:p>.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" office:string-value="Teatro de Verano" table:formula="of:=VLOOKUP(1;1;1)"><text:p>Teatro de Verano</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("Teatro de Verano", hoja.Celda(0, 0).Texto);
    }

    [Fact]
    public void LeerHoja_CeldaStringSinFormula_LeeTextoDelParrafo()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>COLORLUX</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("COLORLUX", hoja.Celda(0, 0).Texto);
    }

    [Fact]
    public void LeerHoja_CeldaStringTipoSinContenido_SeConsideraVacia()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"/>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.True(hoja.Celda(0, 0).EsVacia);
    }

    [Fact]
    public void LeerHoja_CeldaVacia_EsVaciaVerdadero()
    {
        var doc = Documento("<table:table-row><table:table-cell/></table:table-row>");

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.True(hoja.Celda(0, 0).EsVacia);
    }

    [Fact]
    public void LeerHoja_HojaInexistente_LanzaInvalidOperationException()
    {
        var doc = Documento("<table:table-row/>");

        Assert.Throws<InvalidOperationException>(() => OdsContentXmlReader.LeerHoja(doc, "NoExiste"));
    }

    [Fact]
    public void ComoTexto_CeldaNumerica_DevuelveElNumeroComoTexto()
    {
        // Gotcha: FACTURA/ORDEN mezclan float y string en la planilla real (867331 vs "A45735")
        // — se tratan SIEMPRE como texto libre.
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="float" office:value="20207"><text:p>20207</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("20207", hoja.Celda(0, 0).ComoTexto());
    }

    [Fact]
    public void ComoTexto_CeldaDeTexto_DevuelveElTextoDirecto()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>A45735</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("A45735", hoja.Celda(0, 0).ComoTexto());
    }

    [Fact]
    public void CeldasDeFila_DevuelveSoloLasCeldasConContenidoOrdenadasPorColumna()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string"><text:p>COL0</text:p></table:table-cell>
              <table:table-cell/>
              <table:table-cell office:value-type="string"><text:p>COL2</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        var celdas = hoja.CeldasDeFila(0).ToList();

        Assert.Equal(2, celdas.Count);
        Assert.Equal(0, celdas[0].Columna);
        Assert.Equal("COL0", celdas[0].Celda.Texto);
        Assert.Equal(2, celdas[1].Columna);
        Assert.Equal("COL2", celdas[1].Celda.Texto);
    }

    [Fact]
    public void LeerHoja_ColumnasRepetidasConContenido_SoloGuardaElValorEnLaPrimeraColumnaDelRango()
    {
        // Decisión (minor, no observado en las planillas reales soportadas): si una celda con
        // number-columns-repeated>1 trae contenido, ese valor queda SOLO en la primera columna
        // del rango — las columnas cubiertas por la repetición avanzan el índice pero no se
        // materializan. Se fija este comportamiento con un test en vez de cambiarlo porque no
        // rompe la alineación de las columnas siguientes (ver DESPUES en columna 3).
        var doc = Documento("""
            <table:table-row>
              <table:table-cell office:value-type="string" table:number-columns-repeated="3"><text:p>REPETIDA</text:p></table:table-cell>
              <table:table-cell office:value-type="string"><text:p>DESPUES</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal("REPETIDA", hoja.Celda(0, 0).Texto);
        Assert.True(hoja.Celda(0, 1).EsVacia);
        Assert.True(hoja.Celda(0, 2).EsVacia);
        Assert.Equal("DESPUES", hoja.Celda(0, 3).Texto);
    }

    [Fact]
    public void BuscarTexto_DevuelveLaPosicionDeLaPrimeraCoincidencia()
    {
        var doc = Documento("""
            <table:table-row>
              <table:table-cell/>
              <table:table-cell office:value-type="string"><text:p>PRESUPUESTO</text:p></table:table-cell>
            </table:table-row>
            """);

        var hoja = OdsContentXmlReader.LeerHoja(doc, "Hoja1");

        Assert.Equal((0, 1), hoja.BuscarTexto("PRESUPUESTO"));
        Assert.Null(hoja.BuscarTexto("NO_EXISTE"));
    }

    [Fact]
    public void ListarHojas_DevuelveLosNombresDeTodasLasTablas()
    {
        var doc = XDocument.Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content
                xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0">
              <office:body>
                <office:spreadsheet>
                  <table:table table:name="LINEA1"/>
                  <table:table table:name="SALDO TOTALES"/>
                </office:spreadsheet>
              </office:body>
            </office:document-content>
            """);

        var hojas = OdsContentXmlReader.ListarHojas(doc);

        Assert.Equal(new[] { "LINEA1", "SALDO TOTALES" }, hojas);
    }
}
