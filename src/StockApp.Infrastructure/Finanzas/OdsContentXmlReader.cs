using System.Globalization;
using System.Xml.Linq;

namespace StockApp.Infrastructure.Finanzas;

/// <summary>
/// Valor cacheado de una celda ODS ya resuelto (F5a: nunca se evalúan fórmulas, solo se lee
/// lo que LibreOffice/Excel dejó cacheado al guardar el archivo).
/// </summary>
internal sealed record CeldaOds(string? Texto, decimal? Numero, DateOnly? Fecha)
{
    public static readonly CeldaOds Vacia = new(null, null, null);

    public bool EsVacia => Texto is null && Numero is null && Fecha is null;

    /// <summary>
    /// Lee la celda SIEMPRE como texto libre — FACTURA/ORDEN mezclan float y string en la
    /// planilla real (ej. 867331 vs "A45735"), nunca se tratan como número.
    /// </summary>
    public string? ComoTexto() => Texto ?? Numero?.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Grilla de celdas de UNA hoja de un .ods, ya expandida (sin table:number-columns-repeated
/// ni colspan/covered-cell): cada celda con contenido vive en su índice (fila, columna) real
/// de la hoja, 0-based. Las filas de relleno finales (table:number-rows-repeated masivo) NO
/// están incluidas.
/// </summary>
internal sealed class OdsHoja
{
    private readonly Dictionary<(int Fila, int Columna), CeldaOds> _celdas;

    internal OdsHoja(Dictionary<(int, int), CeldaOds> celdas, int cantidadFilas)
    {
        _celdas = celdas;
        CantidadFilas = cantidadFilas;
    }

    /// <summary>Cantidad de filas explícitas leídas (corta antes de la fila de relleno).</summary>
    public int CantidadFilas { get; }

    public CeldaOds Celda(int fila, int columna) =>
        _celdas.TryGetValue((fila, columna), out var celda) ? celda : CeldaOds.Vacia;

    /// <summary>Todas las celdas con contenido de una fila, ordenadas por columna.</summary>
    public IEnumerable<(int Columna, CeldaOds Celda)> CeldasDeFila(int fila) =>
        _celdas.Where(kv => kv.Key.Fila == fila)
               .OrderBy(kv => kv.Key.Columna)
               .Select(kv => (kv.Key.Columna, kv.Value));

    /// <summary>
    /// Posición (fila, columna) de la primera celda (orden fila luego columna) cuyo texto
    /// coincide EXACTO. Null si no aparece. Asume que el texto buscado aparece una sola vez
    /// en la zona relevante de la hoja (cierto para los headers de estas planillas).
    /// </summary>
    public (int Fila, int Columna)? BuscarTexto(string texto) =>
        _celdas.Where(kv => kv.Value.Texto == texto)
               .OrderBy(kv => kv.Key.Fila).ThenBy(kv => kv.Key.Columna)
               .Select(kv => ((int, int)?)(kv.Key.Fila, kv.Key.Columna))
               .FirstOrDefault();
}

/// <summary>
/// Lector de bajo nivel de una hoja dentro del content.xml de un .ods (F5a). Expande
/// table:number-columns-repeated, table:number-columns-spanned (colspan) y
/// table:covered-table-cell, y corta la lectura al llegar a una fila con
/// table:number-rows-repeated GRANDE (ver <see cref="UmbralFilasRellenoFinal"/>): el relleno
/// final real de LibreOffice declara hasta 1.048.576 filas por hoja como UNA sola fila con
/// number-rows-repeated masivo. Bloques number-rows-repeated CHICOS (filas vacías intermedias
/// o al inicio de la hoja, ej. "3") NO cortan la lectura — solo avanzan el índice de fila,
/// preservando los datos reales que vengan después (bug real: PlanillaGastos2026.ods hojas
/// ANUAL/GRAFICAS traen 3 filas vacías al inicio seguidas de la tabla "TOTALES POR RUBRO";
/// cortar ahí perdía toda la tabla). Nunca evalúa fórmulas: siempre lee el valor cacheado
/// (office:value / office:date-value / office:string-value).
/// </summary>
internal static class OdsContentXmlReader
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    /// <summary>
    /// Umbral a partir del cual un table:number-rows-repeated se interpreta como el relleno
    /// final de la hoja y corta la lectura. LibreOffice Calc rellena cada hoja hasta el límite
    /// de ~1.048.576 filas (valores reales observados: 1048375, 1048521); ninguna de las
    /// planillas reales soportadas tiene 1000+ filas vacías/repetidas LEGÍTIMAS dentro de la
    /// zona de datos, así que cualquier repetición por debajo de este umbral es contenido real
    /// (o relleno intermedio chico) y NO debe cortar la lectura.
    /// </summary>
    private const int UmbralFilasRellenoFinal = 1000;

    public static IReadOnlyList<string> ListarHojas(XDocument contentXml) =>
        contentXml.Descendants(Table + "table")
            .Select(t => (string)t.Attribute(Table + "name")!)
            .ToList();

    public static OdsHoja LeerHoja(XDocument contentXml, string nombreHoja)
    {
        var tablaXml = contentXml.Descendants(Table + "table")
            .FirstOrDefault(t => (string?)t.Attribute(Table + "name") == nombreHoja)
            ?? throw new InvalidOperationException($"La hoja '{nombreHoja}' no existe en el .ods.");

        var celdas = new Dictionary<(int, int), CeldaOds>();
        var fila = 0;

        foreach (var filaXml in tablaXml.Elements(Table + "table-row"))
        {
            var filasRepetidas = (int?)filaXml.Attribute(Table + "number-rows-repeated") ?? 1;
            if (filasRepetidas >= UmbralFilasRellenoFinal)
                break; // relleno final real de la hoja — acá SÍ termina la data (no materializar ~1M filas).

            var columna = 0;
            foreach (var celdaXml in filaXml.Elements())
            {
                var esCubierta = celdaXml.Name == Table + "covered-table-cell";
                // El avance de columna se basa SOLO en number-columns-repeated. Cuando una
                // celda trae table:number-columns-spanned="N" (colspan), el XML ya incluye
                // explícitamente N-1 <table:covered-table-cell> a continuación (comprimidos con
                // number-columns-repeated si son varios) — number-columns-spanned es solo
                // metadata y NO debe sumarse al avance, o se duplica y desalinea la columna
                // siguiente (bug detectado por los tests de colspan).
                var avance = (int?)celdaXml.Attribute(Table + "number-columns-repeated") ?? 1;

                if (!esCubierta)
                {
                    var valor = LeerValor(celdaXml);
                    if (!valor.EsVacia)
                    {
                        // Bloque chico repetido (filasRepetidas < umbral): si trae contenido,
                        // se replica en cada una de las filas que representa para no perder
                        // datos ni desalinear el índice de fila de lo que viene después.
                        for (var i = 0; i < filasRepetidas; i++)
                            celdas[(fila + i, columna)] = valor;
                    }
                }

                columna += avance;
            }

            fila += filasRepetidas;
        }

        return new OdsHoja(celdas, fila);
    }

    private static CeldaOds LeerValor(XElement celdaXml)
    {
        var tipo = (string?)celdaXml.Attribute(Office + "value-type");

        return tipo switch
        {
            "float" => new CeldaOds(
                Texto: null,
                Numero: decimal.Parse(
                    (string)celdaXml.Attribute(Office + "value")!,
                    NumberStyles.Float, CultureInfo.InvariantCulture),
                Fecha: null),

            "date" => new CeldaOds(
                Texto: null,
                Numero: null,
                Fecha: DateOnly.ParseExact(
                    (string)celdaXml.Attribute(Office + "date-value")!,
                    "yyyy-MM-dd", CultureInfo.InvariantCulture)),

            "string" => new CeldaOds(
                // Fórmulas de texto (ej. VLOOKUP de RUBRO) cachean el resultado en
                // office:string-value; las celdas de texto planas (sin fórmula) no tienen ese
                // atributo y el valor vive en <text:p>.
                Texto: NuloSiVacio((string?)celdaXml.Attribute(Office + "string-value"))
                    ?? NuloSiVacio(string.Concat(celdaXml.Elements(Text + "p").Select(p => p.Value))),
                Numero: null,
                Fecha: null),

            _ => CeldaOds.Vacia,
        };
    }

    private static string? NuloSiVacio(string? texto) => string.IsNullOrEmpty(texto) ? null : texto;
}
