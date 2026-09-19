using System.Globalization;
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using StockApp.Application.Exportacion;

namespace StockApp.Documentos;

/// <summary>
/// Plantilla tabular genérica para exportar cualquier colección a PDF (spec 2026-09-18): una
/// sola plantilla para las 9 pantallas de este plan (y las ~30 futuras) -- solo cambian título,
/// columnas y datos. El encabezado de la tabla se repite en cada página vía
/// <c>Row.HeadingFormat = true</c>, nativo de MigraDoc.
///
/// Esta clase crece de forma incremental a lo largo de las Tareas 3-7 del plan: acá solo se
/// resuelve la tabla básica con encabezado repetido. El apaisado automático (Tarea 4), el wrap
/// sin truncar (Tarea 5, ya nativo de MigraDoc), el membrete (Tarea 6) y el pie de página
/// (Tarea 7) se agregan sobre este mismo método.
/// </summary>
public sealed class PlantillaTabular
{
    private const string FormatoFecha = "dd/MM/yyyy HH:mm:ss";
    private const string FormatoFechaSolo = "dd/MM/yyyy";

    /// <summary>
    /// Registra <see cref="ResolvedorFuentes"/> como resolver global de PDFsharp la primera vez
    /// que se toca este tipo. Sin esto, CUALQUIER render de texto revienta con
    /// "No appropriate font found" en cualquier plataforma (hallazgo de la Tarea 0).
    ///
    /// Movido acá desde <see cref="PdfExporterMigraDoc"/> en la Tarea 3: esta clase es ahora la
    /// única que efectivamente renderiza (llama a <see cref="Renderizar"/>), y los tests de
    /// <c>PlantillaTabularTests</c> instancian <see cref="PlantillaTabular"/> directo, sin pasar
    /// por <see cref="PdfExporterMigraDoc"/> -- si el registro se hubiera dejado en
    /// <c>PdfExporterMigraDoc</c>, esos tests habrían quedado a merced del orden de ejecución de
    /// xUnit para que el resolver ya estuviera seteado (falso verde intermitente).
    /// </summary>
    static PlantillaTabular()
    {
        GlobalFontSettings.FontResolver ??= new ResolvedorFuentes();
    }

    public byte[] Generar<T>(
        IEnumerable<T> items,
        IReadOnlyList<string> columnas,
        MetadatosDocumento metadatos)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(columnas);
        ArgumentNullException.ThrowIfNull(metadatos);

        var propiedades = ResolverPropiedades<T>(columnas);

        var document = new Document();
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;

        var tabla = section.AddTable();
        tabla.Borders.Width = 0.5;

        foreach (var _ in columnas)
            tabla.AddColumn();

        var filaEncabezado = tabla.AddRow();
        filaEncabezado.HeadingFormat = true;
        filaEncabezado.Format.Font.Bold = true;
        for (var i = 0; i < columnas.Count; i++)
            filaEncabezado.Cells[i].AddParagraph(columnas[i]);

        foreach (var item in items)
        {
            var fila = tabla.AddRow();
            for (var i = 0; i < columnas.Count; i++)
            {
                var valor = propiedades[i].GetValue(item);
                fila.Cells[i].AddParagraph(FormatearValor(valor));
            }
        }

        return Renderizar(document);
    }

    private static PropertyInfo[] ResolverPropiedades<T>(IReadOnlyList<string> columnas)
        => columnas
            .Select(nombre =>
                typeof(T).GetProperty(nombre, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new ArgumentException(
                        $"La propiedad '{nombre}' no existe en {typeof(T).Name}.", nameof(columnas)))
            .ToArray();

    /// <summary>
    /// Mismo criterio de fechas que <c>CsvExporter.FormatearValor</c> (bugfix de huso horario:
    /// DateTime se persiste en UTC pero se muestra en hora local; DateOnly NO se convierte,
    /// representa un día-calendario sin instante). Suma formato invariante explícito para
    /// decimales y enteros: un documento impreso/archivado no puede depender de la cultura del
    /// SO que lo generó (decisión del proyecto: decimales con punto, ver memoria
    /// "Uruguay, no Argentina").
    /// </summary>
    private static string FormatearValor(object? valor) => valor switch
    {
        null => string.Empty,
        DateTime fecha => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString(FormatoFecha, CultureInfo.InvariantCulture),
        DateOnly fecha => fecha.ToString(FormatoFechaSolo, CultureInfo.InvariantCulture),
        decimal numero => numero.ToString("N2", CultureInfo.InvariantCulture),
        int numero => numero.ToString(CultureInfo.InvariantCulture),
        _ => valor.ToString() ?? string.Empty,
    };

    private static byte[] Renderizar(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var ms = new MemoryStream();
        renderer.Save(ms, false);
        return ms.ToArray();
    }
}
