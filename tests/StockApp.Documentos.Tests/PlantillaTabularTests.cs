using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StockApp.Application.Exportacion;
using StockApp.Documentos;
using UglyToad.PdfPig;
using Xunit;

namespace StockApp.Documentos.Tests;

public class PlantillaTabularTests
{
    private sealed record FilaSimple(string Codigo, string Nombre);

    private static MetadatosDocumento Metadatos(string titulo = "Reporte de prueba") =>
        new(titulo, "Sin filtros aplicados.", "admin");

    /// <summary>
    /// Columnas cuyo rótulo coincide con el nombre de la propiedad, para los tests a los que el
    /// rótulo les da igual (orientación, wrap, paginación, formato numérico). El caso en que
    /// rótulo y propiedad DIFIEREN -- que es el punto de <see cref="ColumnaPdf"/> -- lo cubre
    /// <see cref="Generar_ConRotulosDistintosDelNombreDePropiedad_ImprimeElRotulo"/>.
    /// </summary>
    private static ColumnaPdf[] Columnas(params string[] propiedades) =>
        [.. propiedades.Select(nombre => new ColumnaPdf(nombre, nombre))];

    [Fact]
    public void Generar_ConDatos_ElHeaderYLosValoresSonLegibles()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar"), new FilaSimple("P002", "Harina") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Codigo", texto);
        Assert.Contains("Nombre", texto);
        Assert.Contains("P001", texto);
        Assert.Contains("Azúcar", texto);
        Assert.Contains("P002", texto);
        Assert.Contains("Harina", texto);
    }

    [Fact]
    public void Generar_ColeccionVacia_SoloTieneElHeader()
    {
        var items = System.Array.Empty<FilaSimple>();
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("Codigo", texto);
        Assert.Contains("Nombre", texto);
    }

    [Fact]
    public void Generar_ColumnaInexistente_LanzaArgumentException()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var ex = Assert.Throws<ArgumentException>(
            () => plantilla.Generar(items, Columnas("Codigo", "NoExiste"), Metadatos()));

        Assert.Contains("NoExiste", ex.Message);
    }

    /// <summary>
    /// GUARDIÁN de los rótulos (review final, Importante 3): el encabezado que se imprime es el
    /// <see cref="ColumnaPdf.Rotulo"/> (el mismo <c>Header</c> que muestra el DataGrid), NO el
    /// nombre de la propiedad del DTO. Antes la plantilla imprimía el nombre crudo y un reporte
    /// oficial de la Intendencia salía encabezado con "NombreUsuario" o "PorcentajeEjecucion".
    ///
    /// El rótulo lleva tilde y espacio a propósito: es el caso real ("Línea POA", "% Ejecución")
    /// y además hace imposible que el test pase por casualidad si alguien vuelve a imprimir la
    /// propiedad.
    /// </summary>
    [Fact]
    public void Generar_ConRotulosDistintosDelNombreDePropiedad_ImprimeElRotulo()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var columnas = new[]
        {
            new ColumnaPdf(nameof(FilaSimple.Codigo), "Código"),
            new ColumnaPdf(nameof(FilaSimple.Nombre), "Línea POA"),
        };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, columnas, Metadatos());

        var texto = ObtenerTextoConEspacios(pdf);
        Assert.Contains("Código", texto);
        Assert.Contains("Línea POA", texto);
        Assert.DoesNotContain("Codigo", texto);
        // El valor de la fila sigue saliendo de la propiedad, no del rótulo.
        Assert.Contains("P001", texto);
    }

    /// <summary>
    /// Guardián del encabezado repetido (MigraDoc HeadingFormat, nativo): con suficientes filas
    /// para forzar 2+ páginas, "Codigo" tiene que aparecer en AMBAS páginas, no solo en la primera.
    /// </summary>
    [Fact]
    public void Generar_ConSuficientesFilasParaDosPaginas_ElHeaderApareceEnAmbasPaginas()
    {
        var items = Enumerable.Range(1, 80)
            .Select(i => new FilaSimple($"P{i:0000}", $"Producto número {i}"))
            .ToList();
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), Metadatos());

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 2, $"Se esperaban 2+ páginas, hubo {documento.NumberOfPages}.");

        foreach (var pagina in documento.GetPages())
            Assert.Contains("Codigo", pagina.Text);
    }

    /// <summary>Contrato IPdfExporter cumplido por PdfExporterMigraDoc, delegando a PlantillaTabular.</summary>
    [Fact]
    public void PdfExporterMigraDoc_ImplementaIPdfExporter_YGeneraContenidoLegible()
    {
        IPdfExporter exportador = new PdfExporterMigraDoc();
        var items = new[] { new FilaSimple("P001", "Azúcar") };

        var pdf = exportador.Exportar(items, Columnas("Codigo", "Nombre"), Metadatos("Título del exportador"));

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));
        Assert.Contains("P001", texto);
    }

    [Fact]
    public void Generar_ConSeisColumnasOMenos_UsaOrientacionVertical()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.True(pagina.Height > pagina.Width, "6 columnas o menos debe ser A4 vertical.");
    }

    /// <summary>
    /// Borde de la regla de ancho: 7 columnas es el primer valor que dispara apaisado. Usa 7 de
    /// las 11 de <see cref="FilaOnceColumnas"/> a propósito -- el peor caso de 11 columnas lo
    /// cubre <see cref="Generar_ConOnceColumnas_LaTablaEntraEnElAnchoImprimible"/>.
    /// </summary>
    [Fact]
    public void Generar_ConMasDeSeisColumnas_UsaOrientacionApaisada()
    {
        var items = new[] { new FilaOnceColumnas() };
        var columnas = Columnas("C1", "C2", "C3", "C4", "C5", "C6", "C7");
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, columnas, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        Assert.True(pagina.Width > pagina.Height, "Más de 6 columnas debe ser A4 apaisado.");
    }

    /// <summary>
    /// El peor caso de la spec: Gastos, 11 columnas, 4 de ellas de texto largo. El record se
    /// llamaba <c>FilaOnceColumnas</c> pero declaraba SIETE campos (review final, Crítico 2), así
    /// que las 11 columnas no se renderizaron nunca en ningún test de la rama -- y con 11
    /// columnas sin ancho asignado (2,5 cm fijos cada una = 27,5 cm) la tabla se salía del área
    /// imprimible de un A4 apaisado (24,7 cm). Los valores por defecto imitan el contenido real
    /// de la grilla de Gastos (proveedor, detalle largo, línea POA, importes de 7 cifras) para
    /// que el reparto de ancho se ejercite contra longitudes verosímiles, no contra "a", "b", "c".
    /// </summary>
    private sealed record FilaOnceColumnas(
        string C1 = "01/09/2026",
        string C2 = "Ferretería del Centro S.R.L.",
        string C3 = "A-0001234",
        string C4 = "Compra de materiales varios para el mantenimiento del alumbrado público",
        string C5 = "Rentas generales",
        string C6 = "Materiales de construcción",
        string C7 = "Línea POA 3 — Alumbrado público",
        string C8 = "1.234.567,89",
        string C9 = "1.000.000,00",
        string C10 = "234.567,89",
        string C11 = "Parcialmente pagado");

    /// <summary>Los dos márgenes laterales del documento, en puntos PDF (2,5 cm, ver PlantillaTabular).</summary>
    private const double MargenLateralEnPuntos = 2.5 / 2.54 * 72;

    /// <summary>
    /// GUARDIÁN del reparto de ancho de columnas (review final, Crítico 2): con las 11 columnas
    /// del peor caso, NINGÚN contenido puede pasar del margen derecho. MigraDoc no hace auto-fit:
    /// una columna sin <c>Width</c> toma 2,5 cm FIJOS sin mirar el ancho de página, así que
    /// quitar el cálculo de ancho (volver a <c>tabla.AddColumn()</c> sin argumento) empuja las
    /// últimas columnas fuera de la hoja y este test se pone rojo -- verificado por mutación, ver
    /// el reporte del fix.
    ///
    /// Se afirma sobre la palabra MÁS A LA DERECHA de la página (no sobre una columna puntual):
    /// cualquier columna que se desborde la hace fallar, sin que el test tenga que saber cuál.
    /// La tolerancia de 1 pt es por el redondeo de cm a puntos; el padding interno de celda de
    /// MigraDoc juega a favor (mete el texto HACIA ADENTRO del borde de la columna), así que no
    /// hay riesgo de falso verde por ahí.
    /// </summary>
    [Fact]
    public void Generar_ConOnceColumnas_LaTablaEntraEnElAnchoImprimible()
    {
        var items = Enumerable.Range(1, 10).Select(_ => new FilaOnceColumnas()).ToList();
        var columnas = Columnas("C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10", "C11");
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, columnas, Metadatos());

        using var documento = PdfDocument.Open(pdf);
        foreach (var pagina in documento.GetPages())
        {
            var limiteDerecho = pagina.Width - MargenLateralEnPuntos;
            var masADerecha = pagina.GetWords().MaxBy(w => w.BoundingBox.Right);

            Assert.NotNull(masADerecha);
            Assert.True(
                masADerecha.BoundingBox.Right <= limiteDerecho + 1,
                $"En la página {pagina.Number} el contenido llega a x={masADerecha.BoundingBox.Right:0.00} " +
                $"y el margen derecho está en x={limiteDerecho:0.00} (ancho de página {pagina.Width:0.00}). " +
                $"Texto desbordado: '{masADerecha.Text}'.");
        }
    }

    private sealed record FilaNumerica(
        string Codigo, decimal Precio, double Cantidad, long Total, float Peso, decimal MontoGrande);

    /// <summary>
    /// Guardián del formato numérico del documento (review final, Crítico 1): el PDF se imprime
    /// y se archiva al lado de la pantalla de la que salió, así que tiene que decir EXACTAMENTE
    /// lo mismo -- es-UY, separador de miles "." y decimal ",", igual que
    /// <c>MonedaConverter</c> ("C2") y <c>CantidadConverter</c> ("0.####") en las grillas. Antes
    /// de este fix la plantilla formateaba con <c>InvariantCulture</c> y el mismo número salía
    /// con los separadores INVERTIDOS respecto de la pantalla ("26,400.00" en papel contra
    /// "26.400,00" en pantalla).
    ///
    /// Se sigue forzando la cultura del hilo (acá "en-US", que tiene los separadores al revés
    /// que es-UY) porque ese era el valor real del test original y no se pierde: lo que se
    /// afirma es que el formato es FIJO, no que sea el del SO. Con cultura "es-AR" el test no
    /// probaría nada: es-AR y es-UY usan los mismos separadores, así que un
    /// <c>ToString()</c> sin cultura explícita pasaría igual.
    /// </summary>
    [Fact]
    public void Generar_SeaCualSeaLaCulturaDelHilo_FormateaNumerosConLaCulturaDeLaPantalla()
    {
        var culturaOriginal = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var items = new[] { new FilaNumerica("P001", 45.5m, 10.75, 1000L, 3.25f, 1234567.89m) };
            var plantilla = new PlantillaTabular();

            var pdf = plantilla.Generar(
                items,
                Columnas("Codigo", "Precio", "Cantidad", "Total", "Peso", "MontoGrande"),
                Metadatos());

            var texto = ObtenerTextoConEspacios(pdf);

            // Decimales con COMA (es-UY), como en la grilla.
            Assert.Contains("45,50", texto);
            Assert.Contains("10,75", texto);
            Assert.Contains("3,25", texto);

            // Miles con PUNTO (es-UY): sin esto un monto de 7 cifras es ilegible en papel.
            Assert.Contains("1.234.567,89", texto);

            // Los ENTEROS van sin decimales y sin separador de miles: hay columnas enteras que
            // son identificadores (EntidadId de Auditoría), donde "1.000" sería incorrecto.
            Assert.Contains("1000", texto);

            // Y nada del formato invariante que este fix vino a dar vuelta.
            Assert.DoesNotContain("45.50", texto);
            Assert.DoesNotContain("10.75", texto);
            Assert.DoesNotContain("3.25", texto);
            Assert.DoesNotContain("1,234,567.89", texto);
        }
        finally
        {
            CultureInfo.CurrentCulture = culturaOriginal;
        }
    }

    private sealed record FilaConTextoLargo(string Nombre, string Detalle);

    /// <summary>
    /// Guardián de la regla de texto largo (Tarea 5, restricciones globales del plan): un
    /// documento de auditoría que se archiva no puede truncar contenido -- el caso extremo es
    /// <c>Detalle</c> del log de auditoría, campo libre sin tope de longitud. MigraDoc no trunca
    /// texto de celda por diseño (la fila crece en alto), así que este test parte en verde desde
    /// el primer run; la garantía real está en la verificación por mutación documentada en el
    /// reporte de la Tarea 5, no en este ciclo rojo/verde.
    ///
    /// Nota de PdfPig: el texto extraído de una página con wrap puede llegar con espaciado
    /// distinto al original en los límites de línea (documentado en el spike de la Tarea 0),
    /// así que este test NO compara el string completo por igualdad -- afirma por fragmentos
    /// clave (primero, medio, último). No hace falta normalizar espacios en blanco para este
    /// caso puntual: cada fragmento clave (<c>palabra001</c>, <c>palabra030</c>, <c>palabra060</c>)
    /// es un token sin espacios internos, y MigraDoc/PdfPig cortan el wrap ENTRE palabras, nunca
    /// en medio de una -- verificado empíricamente corriendo el assert sin normalizar antes de
    /// commitear (ver reporte de la Tarea 5). Si algún test futuro necesita afirmar sobre una
    /// subcadena que cruza el límite de dos palabras (p. ej. <c>"palabra029 palabra030"</c>), esa
    /// combinación sí puede fallar por el espaciado que introduce el wrap y ahí sí hace falta
    /// normalizar antes de comparar.
    /// </summary>
    [Fact]
    public void Generar_ConTextoMuyLargo_ApareceCompletoSinTruncar()
    {
        var textoLargo = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"palabra{i:000}"));
        var items = new[] { new FilaConTextoLargo("Fila 1", textoLargo) };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Nombre", "Detalle"), Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var texto = string.Join(" ", documento.GetPages().Select(p => p.Text));

        // El contenido completo tiene que estar -- primera palabra, última palabra, y una del
        // medio. Un truncado dejaría "palabra001" pero no "palabra060".
        Assert.Contains("palabra001", texto);
        Assert.Contains("palabra030", texto);
        Assert.Contains("palabra060", texto);
    }

    /// <summary>
    /// Hallazgo de la Tarea 6, no documentado por el spike de la Tarea 0: <c>Page.Text</c> de
    /// PdfPig concatena las palabras de la página SIN espacios entre ellas cuando el contenido
    /// viene de una tabla sin ancho de columna fijo (el caso del membrete) -- el spike y las
    /// tareas anteriores nunca lo notaron porque todos sus asserts comparaban tokens de una sola
    /// palabra (p. ej. "palabra030"), donde la ausencia de espacios alrededor es invisible. Acá
    /// el requisito real es una frase de varias palabras ("INTENDENCIA DE CARMELO", el título,
    /// la descripción de filtros), así que hace falta reconstruir el texto a partir de
    /// <c>Page.GetWords()</c> (que sí segmenta palabras correctamente, verificado imprimiendo el
    /// resultado) y unirlas con espacio explícito.
    /// </summary>
    private static string ObtenerTextoConEspacios(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        return string.Join(" ", documento.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Fact]
    public void Generar_IncluyeElTituloDelMetadato()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), Metadatos("Valorización de inventario"));

        var texto = ObtenerTextoConEspacios(pdf);
        Assert.Contains("Valorización de inventario", texto);
        Assert.Contains("INTENDENCIA DE CARMELO", texto);
    }

    [Fact]
    public void Generar_IncluyeLaDescripcionDeFiltros()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();
        var metadatos = new MetadatosDocumento("Título", "Período: 01/01/2026 a 31/12/2026.", "admin");

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), metadatos);

        var texto = ObtenerTextoConEspacios(pdf);
        Assert.Contains("Período: 01/01/2026 a 31/12/2026.", texto);
    }

    [Fact]
    public void Generar_ElMembreteIncluyeUnaImagen()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), Metadatos());

        using var documento = PdfDocument.Open(pdf);
        var cantidadImagenes = documento.GetPages().Sum(p => p.GetImages().Count());
        Assert.True(cantidadImagenes >= 1, "El membrete debe incluir el logo.");
    }

    /// <summary>
    /// Comportamiento no especificado literalmente por el plan (que solo trae el caso con
    /// descripción no vacía): con <c>DescripcionFiltros</c> vacío, el membrete NO debe reservar
    /// un renglón en blanco -- MigraDoc reserva altura de línea para un párrafo aunque su texto
    /// sea la cadena vacía, así que un <c>AddParagraph(string.Empty)</c> incondicional deja un
    /// "renglón fantasma" entre el título y la tabla de datos.
    ///
    /// No se puede afirmar la ausencia de un renglón vacío comparando texto extraído (una cadena
    /// vacía no deja rastro en el texto se agregue o no el párrafo). Se afirma por geometría,
    /// usando <c>Letter</c>/<c>Word.BoundingBox</c> (técnica mencionada pero no ejercitada por el
    /// spike de la Tarea 0, verificada acá empíricamente; el spike documenta
    /// <c>Letter.GlyphRectangle</c>, pero esa API está obsoleta en la versión de PdfPig usada por
    /// el proyecto -- <c>BoundingBox</c> es su reemplazo con la misma semántica de rectángulo): se
    /// compara la posición vertical del encabezado "Codigo" con y sin descripción de filtros.
    ///
    /// El caso "con filtro" usa el contenido mínimo posible ("x", un solo carácter no blanco) en
    /// vez de una frase real -- deliberado, para aislar el efecto que se quiere probar (que el
    /// párrafo exista o no) del efecto de cuánto texto tenga. Verificado empíricamente con tres
    /// mediciones directas del valor numérico de Y antes de fijar este diseño:
    /// <list type="bullet">
    /// <item>Con contenido real de una sola línea ("Período: enero a marzo.") la comparación
    /// funciona, pero por poco margen: ese texto es unas pocas líneas más corto que el peor caso,
    /// y la mutación (sacar el <c>if</c>) igual deja "sin filtro" por encima de "con filtro" --
    /// margen insuficiente para detectar la regresión real.</item>
    /// <item>Con "x" como contenido mínimo de una sola línea, en código real "sin filtro" da
    /// Y=703.17 (2 líneas: organismo + título) y "con filtro" da Y=692.28 (3 líneas). Mutando el
    /// código (sacando el <c>if</c>, agregando el párrafo vacío incondicionalmente), "sin filtro"
    /// también pasa a dar Y=692.28 -- EXACTAMENTE igual a "con filtro" (los dos casos son ahora
    /// "3 líneas de una sola línea cada una", indistinguibles en altura) -- la aserción de abajo
    /// se vuelve falsa (igual, no mayor) y el test cae en rojo real. Restaurado tras confirmar.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void Generar_ConDescripcionFiltrosVacia_NoDejaUnRenglonFantasmaEnElMembrete()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();
        var metadatosConFiltro = new MetadatosDocumento("Título", "x", "admin");
        var metadatosSinFiltro = new MetadatosDocumento("Título", string.Empty, "admin");

        var pdfConFiltro = plantilla.Generar(items, Columnas("Codigo", "Nombre"), metadatosConFiltro);
        var pdfSinFiltro = plantilla.Generar(items, Columnas("Codigo", "Nombre"), metadatosSinFiltro);

        var yConFiltro = ObtenerYDelEncabezadoCodigo(pdfConFiltro);
        var ySinFiltro = ObtenerYDelEncabezadoCodigo(pdfSinFiltro);

        Assert.True(
            ySinFiltro > yConFiltro,
            $"Sin descripción de filtros, el encabezado debe quedar más arriba (Y mayor). " +
            $"Con filtro: {yConFiltro}, sin filtro: {ySinFiltro}.");
    }

    private static double ObtenerYDelEncabezadoCodigo(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        var pagina = documento.GetPage(1);
        // Se busca la PALABRA "Codigo" (no la primera letra "C" de la página): "CARMELO" en el
        // membrete también empieza con "C" y aparece antes en el documento, así que filtrar por
        // letra suelta agarraba esa "C" fija en vez del encabezado de la tabla que sí se mueve.
        var palabraCodigo = pagina.GetWords().First(w => w.Text == "Codigo");
        return palabraCodigo.BoundingBox.Top;
    }

    /// <summary>
    /// Guardián del pie de página (Tarea 7): fecha/hora de emisión y usuario emisor tienen que
    /// aparecer en el texto del documento. La numeración "Pág. X de Y" se verifica con precisión
    /// (número correcto por página, total real) en
    /// <see cref="Generar_EnDocumentoDeVariasPaginas_ElPieNumeraCadaPaginaCorrectamente"/> --
    /// acá alcanza con un documento de una sola página y contenido mínimo.
    ///
    /// Fecha: se usa <c>FormatoFecha</c> (la misma constante de <see cref="FormatearValor"/>,
    /// "dd/MM/yyyy HH:mm:ss") para el pie -- NO se inventa un tercer formato de fecha. Se afirma
    /// solo por el día (<c>dd/MM/yyyy</c>) para no depender de que el reloj no cruce un segundo
    /// entre el `DateTime.Now` de este test y el de <c>AgregarPie</c>.
    /// </summary>
    [Fact]
    public void Generar_ElPieIncluyeFechaDeEmisionUsuarioEmisorYNumeracionDePagina()
    {
        var items = new[] { new FilaSimple("P001", "Azúcar") };
        var plantilla = new PlantillaTabular();
        var metadatos = new MetadatosDocumento("Título", "Sin filtros aplicados.", "juan.perez");
        var fechaEsperada = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), metadatos);

        var texto = ObtenerTextoConEspacios(pdf);
        Assert.Contains("juan.perez", texto);
        Assert.Contains("Pág.", texto);
        Assert.Contains(fechaEsperada, texto);
    }

    /// <summary>
    /// Guardián central del pie de página: en un documento de 2+ páginas, CADA página muestra su
    /// propio número (la página 2 dice "2", no "1" repetido) y el total coincide con la cantidad
    /// real de páginas del PDF -- resuelto por MigraDoc en el render vía
    /// <c>AddPageField</c>/<c>AddNumPagesField</c>, nunca contando páginas a mano antes de
    /// generar. Se reconstruyen las palabras de cada página con <c>GetWords()</c> (no
    /// <c>Page.Text</c>, que concatena sin espacios en contenido sin ancho de columna fijo --
    /// hallazgo de la Tarea 6) y se ubica el patrón "Pág." &lt;número&gt; "de" &lt;total&gt;.
    /// </summary>
    [Fact]
    public void Generar_EnDocumentoDeVariasPaginas_ElPieNumeraCadaPaginaCorrectamente()
    {
        var items = Enumerable.Range(1, 80)
            .Select(i => new FilaSimple($"P{i:0000}", $"Producto número {i}"))
            .ToList();
        var plantilla = new PlantillaTabular();
        var metadatos = new MetadatosDocumento("Título", "Sin filtros aplicados.", "juan.perez");

        var pdf = plantilla.Generar(items, Columnas("Codigo", "Nombre"), metadatos);

        using var documento = PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 2, $"Se esperaban 2+ páginas, hubo {documento.NumberOfPages}.");

        foreach (var pagina in documento.GetPages())
        {
            var palabras = pagina.GetWords().Select(w => w.Text).ToList();
            Assert.Contains("juan.perez", palabras);

            var indicePag = palabras.IndexOf("Pág.");
            Assert.True(indicePag >= 0, $"La página {pagina.Number} no tiene pie de página.");
            Assert.True(indicePag + 3 < palabras.Count, $"El pie de la página {pagina.Number} está incompleto.");

            var numeroDePagina = palabras[indicePag + 1];
            var separador = palabras[indicePag + 2];
            var totalDePaginas = palabras[indicePag + 3];

            Assert.Equal("de", separador);
            Assert.Equal(pagina.Number.ToString(CultureInfo.InvariantCulture), numeroDePagina);
            Assert.Equal(documento.NumberOfPages.ToString(CultureInfo.InvariantCulture), totalDePaginas);
        }
    }
}
