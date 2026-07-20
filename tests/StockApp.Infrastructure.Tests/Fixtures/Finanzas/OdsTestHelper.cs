using System.Globalization;
using System.IO.Compression;

namespace StockApp.Infrastructure.Tests.Fixtures.Finanzas;

/// <summary>
/// Helper de test compartido: arma un .ods sintético en memoria (zip con content.xml a medida)
/// para los tests de PlanillaOdsParser. Usado por PlanillaOdsParserGastosTests (Task 3) y
/// PlanillaOdsParserPoaTests (Task 4) — extraído acá para no duplicar la misma lógica de
/// ZipArchive en las dos suites (DRY).
/// </summary>
internal static class OdsTestHelper
{
    /// <summary>
    /// Fila de datos POA genérica: FACTURA(colspan 2) ORDEN(2) PROVEEDOR(2) GASTO(4) IMPORTE(2),
    /// igual layout que <c>EncabezadoDatosPoa</c> en PlanillaOdsParserPoaTests. Cualquier
    /// parámetro null se emite como celda vacía. Sirve tanto para movimientos reales como para
    /// simular la fila de TOTAL fantasma (solo <paramref name="importe"/>) que aparece al fondo
    /// de cada hoja de línea, tras un hueco.
    /// </summary>
    public static string FilaPoa(
        string? factura = null, string? orden = null, string? proveedor = null,
        string? gasto = null, decimal? importe = null) => $"""
        <table:table-row>
          {CeldaTexto(factura, 2)}
          {CeldaTexto(orden, 2)}
          {CeldaTexto(proveedor, 2)}
          {CeldaTexto(gasto, 4)}
          {CeldaNumero(importe, 2)}
        </table:table-row>
        """;

    /// <summary>
    /// El HUECO entre los movimientos reales y la fila de TOTAL: una fila totalmente vacía (sin
    /// ninguna celda con valor), como aparece en la planilla real entre el último movimiento y
    /// el total de cada línea.
    /// </summary>
    public static string FilaPoaVacia() => FilaVaciaXml;

    /// <summary>
    /// La fila SEPARADORA entre el encabezado de datos (FACTURA/ORDEN/...) y el primer
    /// movimiento: una fila totalmente vacía, idéntica en forma al hueco pero con rol distinto.
    /// En la planilla real es la fila ~12, justo debajo del header y arriba del primer
    /// movimiento (fila ~13). El parser la ABSORBE (no corta ahí); recién la SIGUIENTE fila
    /// vacía —el hueco que precede a la fila de TOTAL— corta la lectura de la hoja.
    /// </summary>
    public static string FilaSeparadora() => FilaVaciaXml;

    private const string FilaVaciaXml =
        """<table:table-row><table:table-cell table:number-columns-repeated="14"/></table:table-row>""";

    private static string CeldaTexto(string? valor, int colspan) =>
        valor is null
            ? $"""<table:table-cell table:number-columns-spanned="{colspan}"/>{CeldasCubiertas(colspan - 1)}"""
            : $"""<table:table-cell office:value-type="string" table:number-columns-spanned="{colspan}"><text:p>{valor}</text:p></table:table-cell>{CeldasCubiertas(colspan - 1)}""";

    private static string CeldaNumero(decimal? valor, int colspan) =>
        valor is null
            ? $"""<table:table-cell table:number-columns-spanned="{colspan}"/>{CeldasCubiertas(colspan - 1)}"""
            : $"""<table:table-cell office:value-type="float" office:value="{valor.Value.ToString(CultureInfo.InvariantCulture)}" table:number-columns-spanned="{colspan}"><text:p>{valor.Value}</text:p></table:table-cell>{CeldasCubiertas(colspan - 1)}""";

    private static string CeldasCubiertas(int cantidad) =>
        cantidad <= 0 ? "" : $"""<table:covered-table-cell table:number-columns-repeated="{cantidad}"/>""";


    public static MemoryStream CrearOdsFalso(params (string Nombre, string FilasXml)[] hojas)
    {
        var tablas = string.Join("\n", hojas.Select(h => $"""
            <table:table table:name="{h.Nombre}">
              {h.FilasXml}
            </table:table>
            """));

        var contentXml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content
                xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
                xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
                xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body><office:spreadsheet>{tablas}</office:spreadsheet></office:body>
            </office:document-content>
            """;

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = zip.CreateEntry("content.xml");
            using var writer = new StreamWriter(entrada.Open());
            writer.Write(contentXml);
        }
        stream.Position = 0;
        return stream;
    }
}
