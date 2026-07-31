using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockApp.Api.Json;

/// <summary>
/// Normaliza DateTime al LEER JSON (bug real, VPS): si el valor deserializado viene con
/// Kind=Unspecified — por ejemplo <c>"fecha":"2026-01-15"</c>, sin offset ni "Z" — se
/// reinterpreta como UTC medianoche. Las columnas Fecha/FechaVencimiento de Ingresos y
/// Gastos no declaran HasColumnType explícito en AppDbContext, así que Npgsql las mapea a
/// timestamptz; escribir ahí un DateTime Unspecified tira una excepción de Npgsql que
/// ningún handler contemplaba (500 genérico). El dominio de Finanzas no tiene componente
/// horario, así que interpretar una fecha pelada como UTC medianoche es semánticamente
/// correcto — coherente con el DateTime.SpecifyKind(..., Utc) que ya usan los ViewModels
/// del desktop.
///
/// Un DateTime que YA viene con Kind=Utc o Local NO se toca: se respeta el offset que mandó
/// el cliente. Al ESCRIBIR (serializar la respuesta) el formato es idéntico al default de
/// System.Text.Json — Write delega en el mismo WriteStringValue(DateTime) que usa el
/// converter interno, este tipo solo interviene en Read.
///
/// Registrado como JsonConverter&lt;DateTime&gt; (no DateTime?): System.Text.Json envuelve
/// automáticamente el converter de un value type para su variante Nullable&lt;T&gt;, así que
/// cubre también los campos DateTime? (ej. FechaVencimiento) sin declarar un converter aparte.
/// </summary>
public sealed class DateTimeUnspecifiedAsUtcConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var valor = reader.GetDateTime();
        return valor.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(valor, DateTimeKind.Utc)
            : valor;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
