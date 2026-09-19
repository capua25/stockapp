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
        section.PageSetup.Orientation = columnas.Count > 6 ? Orientation.Landscape : Orientation.Portrait;

        AgregarMembrete(section, metadatos);

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

    /// <summary>
    /// Membrete institucional (Tarea 6): logo + "INTENDENCIA DE CARMELO" + título del reporte +
    /// descripción de los filtros aplicados. La descripción de filtros NO es decorativa -- un
    /// PDF que se imprime y se archiva en una administración pública sin aclarar qué universo de
    /// datos representa (rango de fechas, filtros activos) no es auditable.
    ///
    /// Si <see cref="MetadatosDocumento.DescripcionFiltros"/> viene vacío o en blanco, el párrafo
    /// correspondiente NO se agrega: MigraDoc reserva altura de línea para un párrafo aunque su
    /// texto sea la cadena vacía, así que agregarlo incondicionalmente dejaría un renglón en
    /// blanco fantasma entre el título y la tabla de datos (verificado con
    /// <c>Generar_ConDescripcionFiltrosVacia_NoDejaUnRenglonFantasmaEnElMembrete</c>, que compara
    /// la posición del encabezado de la tabla con y sin descripción de filtros).
    /// </summary>
    private static void AgregarMembrete(Section section, MetadatosDocumento metadatos)
    {
        var tablaMembrete = section.AddTable();
        tablaMembrete.Borders.Visible = false;
        tablaMembrete.AddColumn(Unit.FromCentimeter(3));
        tablaMembrete.AddColumn();

        var fila = tablaMembrete.AddRow();

        var logoBase64 = "base64:" + Convert.ToBase64String(RecursosMembrete.ObtenerLogoNegro());
        var imagen = fila.Cells[0].AddImage(logoBase64);
        imagen.Width = Unit.FromCentimeter(2.5);
        imagen.LockAspectRatio = true;

        var celdaTexto = fila.Cells[1];
        celdaTexto.AddParagraph("INTENDENCIA DE CARMELO").Format.Font.Bold = true;
        var parrafoTitulo = celdaTexto.AddParagraph(metadatos.Titulo);
        parrafoTitulo.Format.Font.Size = 14;

        if (!string.IsNullOrWhiteSpace(metadatos.DescripcionFiltros))
        {
            var parrafoFiltros = celdaTexto.AddParagraph(metadatos.DescripcionFiltros);
            parrafoFiltros.Format.Font.Size = 9;
        }

        section.AddParagraph(); // separación antes de la tabla de datos
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
    /// representa un día-calendario sin instante). Formato invariante explícito para TODOS los
    /// tipos numéricos (enteros y de punto flotante): un documento oficial impreso/archivado no
    /// puede depender de la cultura del hilo/SO que lo generó (decisión del proyecto: decimales
    /// con punto, ver memoria "Uruguay, no Argentina", municipio uruguayo, agosto 2026). Sin
    /// esto, <c>valor.ToString()</c> hereda <see cref="CultureInfo.CurrentCulture"/> y un
    /// <c>double</c>/<c>float</c> saldría con coma bajo culturas como "es-AR".
    /// </summary>
    private static string FormatearValor(object? valor) => valor switch
    {
        null => string.Empty,
        DateTime fecha => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString(FormatoFecha, CultureInfo.InvariantCulture),
        DateOnly fecha => fecha.ToString(FormatoFechaSolo, CultureInfo.InvariantCulture),
        decimal numero => numero.ToString("N2", CultureInfo.InvariantCulture),
        double numero => numero.ToString("N2", CultureInfo.InvariantCulture),
        float numero => numero.ToString("N2", CultureInfo.InvariantCulture),
        long numero => numero.ToString(CultureInfo.InvariantCulture),
        int numero => numero.ToString(CultureInfo.InvariantCulture),
        short numero => numero.ToString(CultureInfo.InvariantCulture),
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
