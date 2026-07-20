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
