using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockApp.Api.Json;

/// <summary>
/// Normaliza DateTime al LEER JSON (bug real, VPS + verificación end-to-end del módulo
/// Tareas) para que TODO DateTime que llega en el body de un request termine con
/// Kind=Utc antes de tocar Npgsql: las columnas timestamptz de todo el esquema (Fecha/
/// FechaVencimiento de Ingresos y Gastos, FechaLimite de Tareas, etc.) no aceptan otra
/// cosa. Hay dos casos de entrada que NO vienen en Utc y cada uno se resuelve distinto:
///
/// 1. Kind=Unspecified — ej. <c>"fecha":"2026-01-15"</c>, sin offset ni "Z" — se
///    REINTERPRETA como UTC (DateTime.SpecifyKind, no convierte el reloj). El dominio de
///    Finanzas no tiene componente horario, así que leer una fecha pelada como UTC
///    medianoche es semánticamente correcto — coherente con el
///    DateTime.SpecifyKind(..., Utc) que ya usan los ViewModels del desktop.
///
/// 2. Kind=Local — ej. <c>"fechaLimite":"2026-08-10T15:00:00-03:00"</c>, con un offset
///    explícito NO-UTC — se CONVIERTE con .ToUniversalTime(), que preserva el instante
///    (18:00 UTC, no reinterpreta el reloj a 15:00 UTC). Antes este caso NO se tocaba: el
///    comentario viejo decía que se "respetaba el offset del cliente", pero Npgsql
///    rechaza escribir un DateTime Kind=Local en timestamptz sin importar qué offset
///    tenía originalmente -> 500 genérico para cualquier cliente HTTP que mandara una
///    fecha con offset no-UTC (el desktop no lo disparaba porque ya normalizaba a UTC del
///    lado cliente antes de serializar, ver TareaFormViewModel).
///
/// Kind=Utc no se toca. Al ESCRIBIR (serializar la respuesta) el formato es idéntico al
/// default de System.Text.Json — Write delega en el mismo WriteStringValue(DateTime) que
/// usa el converter interno, este tipo solo interviene en Read.
///
/// Registrado como JsonConverter&lt;DateTime&gt; (no DateTime?): System.Text.Json envuelve
/// automáticamente el converter de un value type para su variante Nullable&lt;T&gt;, así que
/// cubre también los campos DateTime? (ej. FechaVencimiento, FechaLimite) sin declarar un
/// converter aparte.
/// </summary>
public sealed class DateTimeUnspecifiedAsUtcConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var valor = reader.GetDateTime();
        return valor.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(valor, DateTimeKind.Utc),
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => valor,
        };
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
