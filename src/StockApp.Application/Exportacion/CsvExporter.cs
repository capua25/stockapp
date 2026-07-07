using System.Globalization;
using System.Reflection;
using System.Text;

namespace StockApp.Application.Exportacion;

/// <summary>
/// Implementación de <see cref="ICsvExporter"/> conforme a RFC 4180.
/// Correcciones obligatorias del Incremento 6:
/// 1. El string resultante arranca con BOM UTF-8 ('﻿').
/// 2. Cada fila termina en CRLF explícito ('\r\n'); NO se usa AppendLine
///    porque en Linux emite solo '\n' y rompe RFC 4180.
/// </summary>
public sealed class CsvExporter : ICsvExporter
{
    private const char Bom = '\uFEFF';
    private const string Crlf = "\r\n";
    private const string FormatoFecha = "dd/MM/yyyy HH:mm:ss";

    /// <inheritdoc />
    public string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(columnOrder);

        var sb = new StringBuilder();
        sb.Append(Bom);

        // Header: nombres de columnas en el orden provisto, escapados.
        AppendFila(sb, columnOrder.Select(Escapar));

        // Resolución anticipada de PropertyInfo por columna para no resolver por cada fila.
        var propiedades = columnOrder
            .Select(nombre =>
                typeof(T).GetProperty(nombre, BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new ArgumentException(
                        $"La propiedad '{nombre}' no existe en {typeof(T).Name}.", nameof(columnOrder)))
            .ToArray();

        foreach (var item in items)
        {
            var campos = propiedades.Select(prop =>
            {
                var valor = FormatearValor(prop?.GetValue(item));
                return Escapar(valor);
            });

            AppendFila(sb, campos);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formatea el valor de una celda. Caso especial: <see cref="DateTime"/> — se persiste
    /// en UTC (ver <c>DateTime.UtcNow</c> en MovimientoStockService/
    /// MovimientoStockRepository) pero <c>valor.ToString()</c> la emitía cruda, sin
    /// convertir a hora local (bug de huso horario, mismo síntoma que la grilla). Se fuerza
    /// <c>SpecifyKind(Utc)</c> antes de <c>.ToLocalTime()</c> porque, al releer de SQLite vía
    /// EF Core, el <see cref="DateTimeKind"/> vuelve <c>Unspecified</c> aunque el valor sea un
    /// instante UTC real.
    /// </summary>
    private static string FormatearValor(object? valor)
        => valor switch
        {
            null => "",
            DateTime fecha => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString(FormatoFecha, CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? "",
        };

    private static void AppendFila(StringBuilder sb, IEnumerable<string> campos)
    {
        sb.Append(string.Join(",", campos));
        sb.Append(Crlf);
    }

    /// <summary>
    /// Escapa un campo según RFC 4180: si contiene coma, comilla doble o salto
    /// de línea, se envuelve en comillas y las comillas internas se duplican.
    /// Un campo simple o vacío se devuelve tal cual, sin comillas.
    /// </summary>
    private static string Escapar(string campo)
    {
        var necesitaComillas =
            campo.Contains(',') ||
            campo.Contains('"') ||
            campo.Contains('\n') ||
            campo.Contains('\r');

        if (!necesitaComillas)
            return campo;

        return "\"" + campo.Replace("\"", "\"\"") + "\"";
    }
}
