using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace StockApp.Documentos;

/// <summary>
/// Implementación de <c>IPdfExporter</c> (el contrato llega en la Tarea 2) basada en MigraDoc.
/// Por ahora solo expone métodos de humo, usados por los tests de la Tarea 1 para validar el
/// pipeline Document → PdfDocumentRenderer → bytes (y la resolución de fuentes, ver
/// <see cref="ResolvedorFuentes"/>) antes de introducir ningún contrato de negocio.
/// </summary>
public static class PdfExporterMigraDoc
{
    /// <summary>
    /// Registra <see cref="ResolvedorFuentes"/> como resolver global de PDFsharp la primera vez
    /// que se toca este tipo. Sin esto, CUALQUIER render de texto revienta con
    /// "No appropriate font found" en cualquier plataforma (hallazgo de la Tarea 0) — este es el
    /// bloqueante que la Tarea 1 resuelve por fuera del alcance original del plan.
    /// </summary>
    static PdfExporterMigraDoc()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorFuentes();
    }

    /// <summary>Genera un PDF de una sola página con un único párrafo de texto. Descartado por
    /// las tareas siguientes, que reemplazan el cuerpo real vía <c>PlantillaTabular</c>.</summary>
    public static byte[] GenerarPdfDeHumo(string texto)
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph(texto);

        return Renderizar(document);
    }

    /// <summary>
    /// Genera un PDF con un párrafo en texto normal y otro en negrita. Guardián específico del
    /// bloqueante de la Tarea 0: si <see cref="ResolvedorFuentes"/> no está registrado, o si su
    /// mapeo de la cara "Bold" está roto, este método revienta al renderizar el párrafo en
    /// negrita (el encabezado de la tabla, en tareas futuras, se renderiza en negrita).
    /// </summary>
    public static byte[] GenerarPdfDeHumoConNegrita(string textoRegular, string textoNegrita)
    {
        var document = new Document();
        var section = document.AddSection();
        section.AddParagraph(textoRegular);

        var parrafoNegrita = section.AddParagraph();
        parrafoNegrita.AddFormattedText(textoNegrita, TextFormat.Bold);

        return Renderizar(document);
    }

    private static byte[] Renderizar(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var ms = new MemoryStream();
        renderer.Save(ms, false);
        return ms.ToArray();
    }
}
