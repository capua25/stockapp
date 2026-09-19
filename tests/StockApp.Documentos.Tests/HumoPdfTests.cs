using System.Linq;
using StockApp.Documentos;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Documentos.Tests;

/// <summary>
/// Test de humo (Tarea 1): prueba el pipeline completo MigraDoc → bytes → PdfPig ANTES de
/// que exista ningún contrato de negocio (IPdfExporter llega en la Tarea 2). Técnica de assert
/// decidida en la Tarea 0 (docs/superpowers/decisions/2026-09-18-spike-pdf-imagenes-y-assert.md).
/// </summary>
public class HumoPdfTests
{
    [Fact]
    public void GenerarPdfDeUnaPagina_ElTextoEsLegiblePorPdfPig()
    {
        var bytes = PdfExporterMigraDoc.GenerarPdfDeHumo("HUMO-STOCKAPP-DOCUMENTOS");

        using var documento = PdfDocument.Open(bytes);

        Assert.Equal(1, documento.NumberOfPages);
        var texto = documento.GetPages().Single().Text;
        Assert.Contains("HUMO-STOCKAPP-DOCUMENTOS", texto);
    }
}
