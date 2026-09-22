using System.Globalization;
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
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
    /// Ancho de la columna de VALOR en las tablas de resumen (ver <see cref="AgregarResumen"/>):
    /// totales sueltos y mini-tablas de sección. El valor de un total es siempre corto (un
    /// importe, un saldo), así que un ancho fijo alcanza y deja el resto para la etiqueta.
    /// </summary>
    private const double AnchoColumnaValorResumenCm = 4.0;

    /// <summary>
    /// Cotas del ancho DESEADO de una columna (ver <see cref="RepartirAnchoDeColumnas"/>). Son
    /// cotas de lo que la columna PIDE, nunca de lo que se le garantiza: el ancho garantizado es
    /// siempre su PISO medido (ver <see cref="AnchoDeLaPalabraMasLargaCm"/>).
    ///
    /// El mínimo evita que una columna de una sola letra quede en un hilo cuando sobra lugar; el
    /// máximo evita que un único <c>Detalle</c> larguísimo del log de auditoría (campo libre sin
    /// tope) se lleve la página entera y deje al resto en nada.
    ///
    /// OJO: el mínimo NO se aplica al piso. Ese fue exactamente el bug del 2026-09-21 -- se
    /// clampeaba el deseado a 1,5 cm y después se escalaba TODO por
    /// <c>anchoImprimible / suma</c>, así que con un factor de ~0,59 el "mínimo" de 1,5 cm
    /// terminaba valiendo ~0,9 cm reales en el papel.
    /// </summary>
    private const double AnchoMinimoColumnaCm = 1.5;

    private const double AnchoMaximoColumnaCm = 6.0;

    /// <summary>
    /// Tamaño de fuente nominal de la tabla de datos, en puntos. Se SETEA explícitamente en la
    /// tabla (aunque coincide con el default del estilo <c>Normal</c> de MigraDoc) porque
    /// <see cref="RepartirAnchoDeColumnas"/> mide el texto con este tamaño: si el default
    /// cambiara, la medición y el render quedarían desalineados en silencio.
    /// </summary>
    private const double TamanoFuenteNominalPt = 10.0;

    /// <summary>
    /// Piso del tamaño de fuente para el caso extremo de
    /// <see cref="RepartirAnchoDeColumnas"/>. Por debajo de ~7 pt una tabla impresa deja de ser
    /// legible, así que la degradación se corta acá y se prefiere el desborde: nunca truncar.
    /// </summary>
    private const double TamanoFuenteMinimoPt = 7.0;

    /// <summary>
    /// Padding lateral de cada celda, en centímetros. Igual que <see cref="MargenLateralCm"/>,
    /// se SETEA explícitamente porque el reparto de ancho lo reserva en el cálculo: el texto de
    /// una celda no dispone del ancho de la columna sino de ese ancho menos dos paddings.
    /// </summary>
    private const double PaddingCeldaCm = 0.1;

    /// <summary>Ancho del trazo de los bordes de la tabla, en puntos.</summary>
    private const double AnchoBordeCeldaPt = 0.5;

    /// <summary>
    /// Todo lo que una celda le come al ancho de su columna ANTES de que quede lugar para el
    /// texto: los dos paddings laterales, el borde (MigraDoc descuenta media línea de borde de
    /// cada lado del área útil de la celda) y una holgura mínima para el redondeo de cm a puntos.
    ///
    /// El borde y la holgura NO son un detalle cosmético: sin ellos el piso de la columna queda
    /// unos 0,5 pt corto y MigraDoc parte palabras que "según la cuenta" entraban justo -- con la
    /// tabla de Valorización, los códigos de artículo más anchos ("A-0602", "A-0902") salían
    /// cortados en "A-" / "0602" mientras que los más angostos entraban en un renglón. No es un
    /// desborde (el guardián de celda seguía verde), pero es una columna partida al pedo.
    /// </summary>
    private static double AnchoNoUtilizableDeCeldaCm =>
        (2 * PaddingCeldaCm) + (AnchoBordeCeldaPt / PuntosPorCm) + HolguraDeRedondeoCm;

    private const double HolguraDeRedondeoCm = 0.01;

    private const double PuntosPorCm = 72.0 / 2.54;

    /// <summary>
    /// Contexto de medición de texto de PDFsharp. Es <c>[ThreadStatic]</c> porque
    /// <see cref="XGraphics"/> no es thread-safe y xUnit corre las clases de test de un mismo
    /// ensamblado en paralelo.
    /// </summary>
    [ThreadStatic]
    private static XGraphics? _contextoDeMedicion;

    [ThreadStatic]
    private static XFont? _fuenteNormal;

    [ThreadStatic]
    private static XFont? _fuenteNegrita;

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
        IReadOnlyList<ColumnaPdf> columnas,
        MetadatosDocumento metadatos,
        ResumenPdf? resumen = null)
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

        var reparto = RepartirAnchoDeColumnas(columnas, celdas, anchoImprimibleCm);

        var tabla = section.AddTable();
        tabla.Borders.Width = AnchoBordeCeldaPt;

        // La familia y el tamaño se fijan acá, no se heredan del estilo `Normal`: son los mismos
        // con los que `RepartirAnchoDeColumnas` MIDIÓ el texto, y esa correspondencia es la que
        // sostiene la garantía de que ninguna palabra se sale de su columna.
        tabla.Format.Font.Name = ResolvedorFuentes.NombreFamilia;
        tabla.Format.Font.Size = Unit.FromPoint(reparto.TamanoFuentePt);
        tabla.LeftPadding = Unit.FromCentimeter(PaddingCeldaCm);
        tabla.RightPadding = Unit.FromCentimeter(PaddingCeldaCm);

        foreach (var anchoCm in reparto.AnchosCm)
            tabla.AddColumn(Unit.FromCentimeter(anchoCm));

        var filaEncabezado = tabla.AddRow();
        filaEncabezado.HeadingFormat = true;
        filaEncabezado.Format.Font.Bold = true;
        for (var i = 0; i < columnas.Count; i++)
            filaEncabezado.Cells[i].AddParagraph(columnas[i].Rotulo);

        foreach (var valores in celdas)
        {
            var fila = tabla.AddRow();
            for (var i = 0; i < columnas.Count; i++)
                fila.Cells[i].AddParagraph(valores[i]);
        }

        if (resumen is not null)
            AgregarResumen(section, resumen, anchoImprimibleCm);

        return Renderizar(document);
    }

    /// <summary>
    /// Resumen de totales (spec de totales/resumen, 2026-09-22): se imprime DESPUÉS de la tabla
    /// principal, dentro de la misma sección -- el pie de página (<see cref="AgregarPie"/>) ya
    /// está seteado como footer de sección y se sigue repitiendo solo, y si el resumen empuja
    /// contenido a una página nueva, el encabezado de la tabla de datos NO se repite acá (ya
    /// terminó), que es lo correcto.
    ///
    /// Los totales sueltos (<see cref="ResumenPdf.Totales"/>) van con estilo de TOTAL: negrita y
    /// una línea de cierre arriba de la primera fila, para que se lea como el cierre de la tabla
    /// de arriba y no como una fila más. Las secciones tituladas van debajo, cada una en su
    /// propia mini-tabla con borde (agrupa visualmente sus filas) precedida por su título en
    /// negrita.
    /// </summary>
    private static void AgregarResumen(Section section, ResumenPdf resumen, double anchoImprimibleCm)
    {
        if (resumen.Totales.Count > 0)
            AgregarTablaDeTotales(section, resumen.Totales, anchoImprimibleCm);

        foreach (var seccion in resumen.Secciones)
            AgregarSeccionDeResumen(section, seccion, anchoImprimibleCm);
    }

    private static void AgregarTablaDeTotales(Section section, IReadOnlyList<TotalPdf> totales, double anchoImprimibleCm)
    {
        section.AddParagraph(); // separación de la tabla de datos

        var tabla = section.AddTable();
        tabla.Borders.Visible = false;
        tabla.AddColumn(Unit.FromCentimeter(anchoImprimibleCm - AnchoColumnaValorResumenCm));
        tabla.AddColumn(Unit.FromCentimeter(AnchoColumnaValorResumenCm));

        for (var i = 0; i < totales.Count; i++)
        {
            var fila = tabla.AddRow();
            fila.Format.Font.Bold = true;
            if (i == 0)
                fila.Borders.Top.Width = Unit.FromPoint(AnchoBordeCeldaPt);

            fila.Cells[0].AddParagraph(totales[i].Etiqueta);
            var celdaValor = fila.Cells[1];
            celdaValor.AddParagraph(FormatearValor(totales[i].Valor));
            celdaValor.Format.Alignment = ParagraphAlignment.Right;
        }
    }

    private static void AgregarSeccionDeResumen(Section section, SeccionResumenPdf seccion, double anchoImprimibleCm)
    {
        var titulo = section.AddParagraph(seccion.Titulo);
        titulo.Format.SpaceBefore = Unit.FromCentimeter(0.4);
        titulo.Format.Font.Bold = true;

        var tabla = section.AddTable();
        tabla.Borders.Width = AnchoBordeCeldaPt;
        tabla.AddColumn(Unit.FromCentimeter(anchoImprimibleCm - AnchoColumnaValorResumenCm));
        tabla.AddColumn(Unit.FromCentimeter(AnchoColumnaValorResumenCm));

        foreach (var total in seccion.Filas)
        {
            var fila = tabla.AddRow();
            fila.Cells[0].AddParagraph(total.Etiqueta);
            var celdaValor = fila.Cells[1];
            celdaValor.AddParagraph(FormatearValor(total.Valor));
            celdaValor.Format.Alignment = ParagraphAlignment.Right;
        }
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
    ///
    /// ==================== EL BUG DEL 2026-09-21 Y SU ARREGLO ====================
    ///
    /// Repartir TODO en proporción no alcanza, y eso rompía el documento en casi todas las filas
    /// de Gastos: cuando el contenido de una celda es UNA SOLA PALABRA SIN ESPACIOS (una fecha,
    /// un número de factura, un nombre de enum, un importe), MigraDoc no tiene dónde cortar la
    /// línea, así que en vez de envolver IMPRIME EL TEXTO ENCIMA DE LA COLUMNA VECINA
    /// ("12/01/2Ø26Fₑrretería El Tornillo S.R.L.", "ModificacionPermisosUsuarioUsuario",
    /// "12.450,0099.600,00"). El piso de <see cref="AnchoMinimoColumnaCm"/> no lo evitaba porque
    /// se aplicaba sobre el ancho DESEADO, ANTES de escalar: con 11 columnas y 5 de texto libre
    /// en el techo el factor daba ~0,59 y el piso de 1,5 cm valía ~0,9 cm en el papel.
    ///
    /// El reparto de ahora distingue dos cosas distintas por columna, las dos MEDIDAS sobre el
    /// contenido real (no por nombre de propiedad ni por tipo CLR):
    ///
    /// - PISO: el ancho de su palabra más larga. Es lo que MigraDoc no puede partir, así que es
    ///   lo único que evita el desborde. Toda columna lo tiene garantizado.
    /// - DESEADO: el ancho de su celda más larga completa, acotado por
    ///   <see cref="AnchoMinimoColumnaCm"/>/<see cref="AnchoMaximoColumnaCm"/>. Es lo que la
    ///   columna necesitaría para NO envolver nunca.
    ///
    /// Primero se garantizan los pisos; el sobrante se reparte en proporción a la ELASTICIDAD de
    /// cada columna (deseado - piso). Una columna irreducible (fecha, código, importe) tiene
    /// elasticidad ~0 y se queda en su piso; una elástica (Detalle, Proveedor, Línea POA) se
    /// lleva el sobrante y sigue envolviendo prolijo, que es como ya funcionaba bien.
    ///
    /// Los anchos se MIDEN con la fuente real (<see cref="MedirAnchoDeTextoCm"/>) en vez de
    /// estimarse por cantidad de caracteres: la estimación de 0,2 cm por carácter servía cuando
    /// el resultado se normalizaba en proporción (solo importaba la relación entre columnas),
    /// pero acá el piso es una GARANTÍA absoluta sobre el papel, y una garantía calculada con un
    /// promedio no es una garantía ("A-000123" y "llllllll" tienen 8 caracteres y anchos
    /// distintos).
    ///
    /// CASO EXTREMO -- si ni siquiera los pisos entran en la hoja, no hay reparto posible. Se
    /// baja el tamaño de fuente de la tabla lo justo para que entren (el ancho del texto es
    /// EXACTAMENTE lineal en el tamaño de fuente, así que el factor se calcula de una y sin
    /// iterar), con piso en <see cref="TamanoFuenteMinimoPt"/>. Si ni con la fuente mínima
    /// entran, se reparte en proporción a los pisos y se acepta el desborde como mal menor:
    /// TRUNCAR no es una opción -- la spec lo prohíbe y lo custodia
    /// <c>Generar_ConTextoMuyLargo_ApareceCompletoSinTruncar</c>. Entre un documento con texto
    /// pisado (feo pero completo y verificable contra la pantalla) y uno con datos amputados
    /// (prolijo y mentiroso) en una administración pública, el feo gana.
    /// </summary>
    private sealed record RepartoDeColumnas(double[] AnchosCm, double TamanoFuentePt);

    private static RepartoDeColumnas RepartirAnchoDeColumnas(
        IReadOnlyList<ColumnaPdf> columnas,
        IReadOnlyList<string[]> celdas,
        double anchoImprimibleCm)
    {
        if (columnas.Count == 0)
            return new RepartoDeColumnas([], TamanoFuenteNominalPt);

        // Anchos de TEXTO (sin padding) medidos al tamaño nominal. El rótulo se mide en negrita
        // porque la fila de encabezado se imprime en negrita, y la negrita es más ancha.
        var pisoDeTextoCm = new double[columnas.Count];
        var deseadoDeTextoCm = new double[columnas.Count];

        for (var i = 0; i < columnas.Count; i++)
        {
            pisoDeTextoCm[i] = AnchoDeLaPalabraMasLargaCm(columnas[i].Rotulo, negrita: true);
            deseadoDeTextoCm[i] = MedirAnchoDeTextoCm(columnas[i].Rotulo, negrita: true);

            foreach (var fila in celdas)
            {
                pisoDeTextoCm[i] = Math.Max(pisoDeTextoCm[i], AnchoDeLaPalabraMasLargaCm(fila[i], negrita: false));
                deseadoDeTextoCm[i] = Math.Max(deseadoDeTextoCm[i], MedirAnchoDeTextoCm(fila[i], negrita: false));
            }
        }

        var reservadoPorColumnaCm = AnchoNoUtilizableDeCeldaCm;
        var anchoDisponibleParaTextoCm = anchoImprimibleCm - (columnas.Count * reservadoPorColumnaCm);
        var sumaDePisosDeTextoCm = pisoDeTextoCm.Sum();

        var tamanoFuentePt = TamanoFuenteNominalPt;
        if (sumaDePisosDeTextoCm > anchoDisponibleParaTextoCm && anchoDisponibleParaTextoCm > 0)
        {
            tamanoFuentePt = Math.Max(
                TamanoFuenteMinimoPt,
                TamanoFuenteNominalPt * (anchoDisponibleParaTextoCm / sumaDePisosDeTextoCm));
        }

        var escala = tamanoFuentePt / TamanoFuenteNominalPt;

        var piso = new double[columnas.Count];
        var deseado = new double[columnas.Count];
        for (var i = 0; i < columnas.Count; i++)
        {
            piso[i] = (pisoDeTextoCm[i] * escala) + reservadoPorColumnaCm;

            var pedido = Math.Min(
                (deseadoDeTextoCm[i] * escala) + reservadoPorColumnaCm, AnchoMaximoColumnaCm);
            deseado[i] = Math.Max(piso[i], Math.Max(pedido, AnchoMinimoColumnaCm));
        }

        var sumaDePisos = piso.Sum();

        // Ni con la fuente mínima entran los pisos: se acepta el desborde (ver el comentario del
        // caso extremo). El `<= 0` es la defensa contra una tabla con todas las columnas vacías.
        if (sumaDePisos >= anchoImprimibleCm || sumaDePisos <= 0)
        {
            var factor = anchoImprimibleCm / (sumaDePisos > 0 ? sumaDePisos : columnas.Count);
            return new RepartoDeColumnas([.. piso.Select(p => (p > 0 ? p : 1) * factor)], tamanoFuentePt);
        }

        var sobrante = anchoImprimibleCm - sumaDePisos;
        double[] elasticidad = [.. Enumerable.Range(0, columnas.Count).Select(i => deseado[i] - piso[i])];
        var sumaDeElasticidades = elasticidad.Sum();

        // Sin ninguna columna elástica (todo el contenido es de una sola palabra y entra) el
        // sobrante igual se reparte, en proporción al piso, para que la tabla ocupe el ancho de
        // la hoja como se espera de un reporte impreso.
        var pesos = sumaDeElasticidades > 0 ? elasticidad : piso;
        var sumaDePesos = sumaDeElasticidades > 0 ? sumaDeElasticidades : sumaDePisos;

        return new RepartoDeColumnas(
            [.. Enumerable.Range(0, columnas.Count).Select(i => piso[i] + (sobrante * (pesos[i] / sumaDePesos)))],
            tamanoFuentePt);
    }

    /// <summary>
    /// Ancho de la palabra más larga de un texto: el PISO de la columna que lo contiene. MigraDoc
    /// solo corta el renglón en un espacio, así que una palabra más ancha que su columna no
    /// envuelve -- se imprime encima de la vecina.
    ///
    /// Se parte por cualquier espacio en blanco (<c>Split</c> sin separadores), no solo por " ":
    /// un valor que venga con tabulación o salto de línea también le da a MigraDoc dónde cortar.
    /// </summary>
    private static double AnchoDeLaPalabraMasLargaCm(string texto, bool negrita)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return 0;

        var maximo = 0.0;
        foreach (var palabra in texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            maximo = Math.Max(maximo, MedirAnchoDeTextoCm(palabra, negrita));

        return maximo;
    }

    /// <summary>
    /// Ancho real del texto en centímetros, medido con la MISMA fuente con la que se va a
    /// imprimir (Inter, <see cref="TamanoFuenteNominalPt"/>) en vez de estimarlo por cantidad de
    /// caracteres. <see cref="ResolvedorFuentes"/> mapea CUALQUIER familia a Inter, así que el
    /// nombre de familia que se le pase a <see cref="XFont"/> no cambia las métricas; se usa el
    /// nombre real igual para que el código diga la verdad.
    /// </summary>
    private static double MedirAnchoDeTextoCm(string texto, bool negrita)
    {
        if (string.IsNullOrEmpty(texto))
            return 0;

        _contextoDeMedicion ??= XGraphics.CreateMeasureContext(
            new XSize(1000, 1000), XGraphicsUnit.Point, XPageDirection.Downwards);

        var fuente = negrita
            ? _fuenteNegrita ??= new XFont(ResolvedorFuentes.NombreFamilia, TamanoFuenteNominalPt, XFontStyleEx.Bold)
            : _fuenteNormal ??= new XFont(ResolvedorFuentes.NombreFamilia, TamanoFuenteNominalPt, XFontStyleEx.Regular);

        return _contextoDeMedicion.MeasureString(texto, fuente).Width / PuntosPorCm;
    }

    private static PropertyInfo[] ResolverPropiedades<T>(IReadOnlyList<ColumnaPdf> columnas)
        => columnas
            .Select(columna =>
                typeof(T).GetProperty(columna.Propiedad, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new ArgumentException(
                        $"La propiedad '{columna.Propiedad}' no existe en {typeof(T).Name}.", nameof(columnas)))
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
