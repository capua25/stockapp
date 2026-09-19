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

    /// <summary>Separador de miles y 2 decimales fijos, igual que la grilla (ver <see cref="FormatearValor"/>).</summary>
    private const string FormatoNumerico = "N2";

    /// <summary>Ver <see cref="CrearCulturaDocumento"/>.</summary>
    private static readonly IFormatProvider CulturaDocumento = CrearCulturaDocumento();

    /// <summary>
    /// Umbral de la regla de ancho de la spec: hasta esta cantidad de columnas el documento sale
    /// A4 vertical; a partir de una más, apaisado. Era un literal suelto en
    /// <see cref="Generar{T}"/> (review final, menor 1).
    /// </summary>
    private const int MaximoColumnasEnVertical = 6;

    /// <summary>
    /// Margen izquierdo y derecho, en centímetros. Se SETEA explícitamente en el
    /// <see cref="PageSetup"/> (aunque coincide con el default de MigraDoc) porque el reparto de
    /// ancho de las columnas lo resta del ancho de página: si el default cambiara, el cálculo
    /// quedaría en silencio desalineado de la página real.
    /// </summary>
    private const double MargenLateralCm = 2.5;

    /// <summary>Ancho de la columna del logo en la tabla del membrete (ver <see cref="AgregarMembrete"/>).</summary>
    private const double AnchoColumnaLogoCm = 3.0;

    /// <summary>
    /// Cotas del ancho "deseado" de una columna ANTES de normalizar al ancho imprimible (ver
    /// <see cref="RepartirAnchoDeColumnas"/>). El mínimo evita que una columna de una sola letra
    /// quede en un hilo; el máximo evita que un único <c>Detalle</c> larguísimo del log de
    /// auditoría (campo libre sin tope) se lleve la página entera y deje al resto en nada.
    /// </summary>
    private const double AnchoMinimoColumnaCm = 1.5;

    private const double AnchoMaximoColumnaCm = 6.0;

    /// <summary>
    /// Centímetros por carácter usados para estimar el ancho deseado de cada columna. Es una
    /// aproximación del ancho medio de un carácter de Inter a 10pt (la fuente del documento, ver
    /// <see cref="ResolvedorFuentes"/>); NO pretende ser exacta, y no hace falta que lo sea: el
    /// resultado se normaliza proporcionalmente al ancho imprimible, así que lo único que
    /// importa es la relación entre columnas, no el valor absoluto.
    /// </summary>
    private const double AnchoPorCaracterCm = 0.2;

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

        // Las filas se materializan YA FORMATEADAS porque se recorren dos veces: una para medir
        // el contenido y repartir el ancho de las columnas, otra para escribir las celdas.
        // `items` puede ser un iterador perezoso de un solo uso (Reporte de tareas pasa el
        // resultado de un método con `yield`), así que enumerarlo dos veces no es una opción.
        var celdas = items
            .Select(item => propiedades.Select(p => FormatearValor(p.GetValue(item))).ToArray())
            .ToList();

        var apaisado = columnas.Count > MaximoColumnasEnVertical;

        var document = new Document();
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = apaisado ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(MargenLateralCm);
        section.PageSetup.RightMargin = Unit.FromCentimeter(MargenLateralCm);

        var anchoImprimibleCm = CalcularAnchoImprimibleCm(apaisado);

        AgregarMembrete(section, metadatos, anchoImprimibleCm);
        AgregarPie(section, metadatos.UsuarioEmisor);

        var tabla = section.AddTable();
        tabla.Borders.Width = 0.5;

        foreach (var anchoCm in RepartirAnchoDeColumnas(columnas, celdas, anchoImprimibleCm))
            tabla.AddColumn(Unit.FromCentimeter(anchoCm));

        var filaEncabezado = tabla.AddRow();
        filaEncabezado.HeadingFormat = true;
        filaEncabezado.Format.Font.Bold = true;
        for (var i = 0; i < columnas.Count; i++)
            filaEncabezado.Cells[i].AddParagraph(columnas[i]);

        foreach (var valores in celdas)
        {
            var fila = tabla.AddRow();
            for (var i = 0; i < columnas.Count; i++)
                fila.Cells[i].AddParagraph(valores[i]);
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
    private static void AgregarMembrete(Section section, MetadatosDocumento metadatos, double anchoImprimibleCm)
    {
        var tablaMembrete = section.AddTable();
        tablaMembrete.Borders.Visible = false;
        tablaMembrete.AddColumn(Unit.FromCentimeter(AnchoColumnaLogoCm));
        // La columna del texto se lleva TODO el ancho restante. Sin ancho explícito tomaba el
        // default de MigraDoc (2,5 cm), así que el bloque "INTENDENCIA DE CARMELO" + título +
        // filtros quedaba más angosto que el propio logo y envolvía en un chorizo vertical
        // (review final, Crítico 2).
        tablaMembrete.AddColumn(Unit.FromCentimeter(anchoImprimibleCm - AnchoColumnaLogoCm));

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

    /// <summary>
    /// Pie de página (Tarea 7): fecha/hora de emisión, usuario emisor y numeración "Pág. X de Y".
    /// Trazabilidad de administración pública -- quién emitió el documento, cuándo, y si al
    /// expediente le falta una hoja (detectable porque el total real de páginas queda impreso).
    ///
    /// <c>section.Footers.Primary</c> se repite automáticamente en TODAS las páginas de la
    /// sección (comportamiento nativo de MigraDoc, igual que <c>HeadingFormat</c> para el
    /// encabezado de tabla en <see cref="Generar{T}"/>) -- no hace falta agregarlo por página.
    ///
    /// El número de página actual y el total se resuelven en el RENDER con
    /// <see cref="ParagraphElements.AddPageField"/> y
    /// <see cref="ParagraphElements.AddNumPagesField"/>: la cantidad de páginas no se conoce
    /// hasta que MigraDoc terminó de paginar el documento completo, así que contarlas a mano acá
    /// (antes de renderizar) sería imposible sin renderizar dos veces.
    ///
    /// La fecha usa <see cref="FormatoFecha"/> (la misma constante que <see cref="FormatearValor"/>
    /// para <c>DateTime</c>) -- no se introduce un tercer formato de fecha en la plantilla.
    /// </summary>
    private static void AgregarPie(Section section, string usuarioEmisor)
    {
        var parrafo = section.Footers.Primary.AddParagraph();
        parrafo.Format.Font.Size = 8;
        parrafo.AddText(
            $"Emitido el {DateTime.Now.ToString(FormatoFecha, CultureInfo.InvariantCulture)} " +
            $"por {usuarioEmisor}   —   Pág. ");
        parrafo.AddPageField();
        parrafo.AddText(" de ");
        parrafo.AddNumPagesField();
    }

    /// <summary>
    /// Ancho útil de la página: el ancho del papel menos los dos márgenes laterales. El tamaño
    /// del papel se pide a MigraDoc (<see cref="PageSetup.GetPageSize"/>) en vez de hardcodear
    /// 21/29,7 cm, así que si algún día cambia el <see cref="PageFormat"/> del documento el
    /// reparto de columnas lo sigue solo.
    /// </summary>
    private static double CalcularAnchoImprimibleCm(bool apaisado)
    {
        PageSetup.GetPageSize(PageFormat.A4, out var anchoVertical, out var altoVertical);
        var anchoPaginaCm = (apaisado ? altoVertical : anchoVertical).Centimeter;
        return anchoPaginaCm - (2 * MargenLateralCm);
    }

    /// <summary>
    /// Reparte el ancho imprimible entre las columnas (review final, Crítico 2). Antes de esto
    /// las columnas se agregaban SIN ancho, así que cada una tomaba el default FIJO de MigraDoc
    /// (2,5 cm) sin mirar el papel: MigraDoc no hace auto-fit como una tabla HTML. Con 11
    /// columnas (Gastos) la tabla pedía 27,5 cm y el área imprimible de un A4 apaisado son 24,7:
    /// las últimas columnas se imprimían fuera de la hoja.
    ///
    /// El reparto es PROPORCIONAL AL CONTENIDO REAL, no parejo: se estima un ancho "deseado" por
    /// columna a partir de la cantidad máxima de caracteres que tiene que mostrar (contando su
    /// encabezado y todas sus celdas), se acota entre
    /// <see cref="AnchoMinimoColumnaCm"/> y <see cref="AnchoMaximoColumnaCm"/>, y se escala todo
    /// por un único factor para que la suma dé EXACTAMENTE el ancho imprimible.
    ///
    /// Medir el contenido es la única forma CONFIABLE de distinguir una columna ancha de una
    /// angosta acá: la plantilla no conoce la semántica de negocio de las columnas, y las dos
    /// alternativas fallan. Por NOMBRE es una heurística frágil (<c>Total</c> es plata en Gastos
    /// y un conteo de tareas en Reporte de tareas). Por TIPO CLR tampoco alcanza: en Auditoría
    /// <c>Detalle</c> (texto libre sin tope) y <c>Accion</c> (una palabra) son las dos
    /// <c>string</c> y quedarían igual de anchas, que es justo el problema que el review marcó.
    /// Los valores, en cambio, ya los tenemos y no mienten.
    /// </summary>
    private static double[] RepartirAnchoDeColumnas(
        IReadOnlyList<string> columnas,
        IReadOnlyList<string[]> celdas,
        double anchoImprimibleCm)
    {
        if (columnas.Count == 0)
            return [];

        var deseados = new double[columnas.Count];
        for (var i = 0; i < columnas.Count; i++)
        {
            var caracteres = columnas[i].Length;
            foreach (var fila in celdas)
                caracteres = Math.Max(caracteres, fila[i].Length);

            deseados[i] = Math.Clamp(
                caracteres * AnchoPorCaracterCm, AnchoMinimoColumnaCm, AnchoMaximoColumnaCm);
        }

        // La cota inferior garantiza suma > 0 con al menos una columna, así que no hay división
        // por cero. El factor puede ser > 1 (contenido angosto: la tabla se estira hasta ocupar
        // el ancho de la hoja, como se espera de un reporte impreso) o < 1 (contenido ancho:
        // todo se comprime en proporción y el wrap de MigraDoc absorbe el resto sin truncar).
        var factor = anchoImprimibleCm / deseados.Sum();
        return [.. deseados.Select(deseado => deseado * factor)];
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
    /// representa un día-calendario sin instante).
    ///
    /// NUMÉRICOS: cultura FIJA es-UY, la MISMA que usan las grillas de la app vía
    /// <c>MonedaConverter</c> ("C2", ej. "$ 26.400,00") y <c>CantidadConverter</c> ("0.####",
    /// ej. "22,5") -- separador de miles "." y decimal ",". El PDF se imprime y se archiva al
    /// lado de la pantalla de la que salió: si el papel dice "26,400.00" y la pantalla
    /// "26.400,00", el mismo número se lee distinto en los dos soportes (hallazgo del review
    /// final, decisión del usuario: manda la pantalla).
    ///
    /// La cultura es FIJA, no la del hilo/SO (mismo criterio y mismo fallback manual que los
    /// dos converters, ver <see cref="CrearCulturaDocumento"/>): un documento oficial no puede
    /// cambiar de formato según la máquina que lo generó.
    ///
    /// Formato ÚNICO "N2" para todo decimal/double/float, sin distinguir moneda de cantidad:
    /// la plantilla recibe nombres de propiedad y valores, no semántica de negocio (ver el
    /// comentario de <see cref="Generar{T}"/> sobre el símbolo "$"). Entre los dos formatos de
    /// la pantalla, el de moneda es el único que sirve para ambos casos: "0.####" no lleva
    /// separador de miles y rendiría 1234567.89m como "1234567,89" (ilegible en un documento
    /// contable), mientras que "N2" sobre una cantidad solo agrega ceros de relleno
    /// ("22,5" en pantalla → "22,50" en papel): mismo número, misma lectura.
    ///
    /// Los ENTEROS se formatean sin decimales y sin separador de miles, igual que
    /// <c>CantidadConverter</c> con enteros ("0.####" no agrupa) -- además hay columnas enteras
    /// que son IDENTIFICADORES, no cantidades (<c>EntidadId</c> en Auditoría): "1.234" como ID
    /// sería directamente incorrecto.
    /// </summary>
    private static string FormatearValor(object? valor) => valor switch
    {
        null => string.Empty,
        DateTime fecha => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString(FormatoFecha, CultureInfo.InvariantCulture),
        DateOnly fecha => fecha.ToString(FormatoFechaSolo, CultureInfo.InvariantCulture),
        decimal numero => numero.ToString(FormatoNumerico, CulturaDocumento),
        double numero => numero.ToString(FormatoNumerico, CulturaDocumento),
        float numero => numero.ToString(FormatoNumerico, CulturaDocumento),
        long numero => numero.ToString(CulturaDocumento),
        int numero => numero.ToString(CulturaDocumento),
        short numero => numero.ToString(CulturaDocumento),
        _ => valor.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Cultura de los números del documento: es-UY, la misma que las grillas. El fallback manual
    /// está copiado del criterio de <c>MonedaConverter</c>/<c>CantidadConverter</c>: si el
    /// runtime corre con ICU deshabilitada (<c>InvariantGlobalization</c>), "es-UY" no existe y
    /// <see cref="CultureInfo.GetCultureInfo"/> tira -- con el <see cref="NumberFormatInfo"/>
    /// armado a mano el formato sigue siendo el mismo sin depender de que el SO tenga la
    /// cultura instalada.
    /// </summary>
    private static IFormatProvider CrearCulturaDocumento()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
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
