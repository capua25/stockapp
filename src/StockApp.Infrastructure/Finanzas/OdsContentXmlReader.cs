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
/// table:covered-table-cell, y corta la lectura en la primera fila con
/// table:number-rows-repeated (gotcha: LibreOffice declara hasta 1.048.576 filas por hoja,
/// pero solo unas pocas tienen datos; el resto es UNA fila con number-rows-repeated masivo).
/// Nunca evalúa fórmulas: siempre lee el valor cacheado (office:value / office:date-value /
/// office:string-value).
/// </summary>
internal static class OdsContentXmlReader
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

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
            if (filasRepetidas > 1)
                break; // fila de relleno vacía hasta el límite de la hoja — acá termina la data real.

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
                        celdas[(fila, columna)] = valor;
                }

                columna += avance;
            }

            fila++;
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
