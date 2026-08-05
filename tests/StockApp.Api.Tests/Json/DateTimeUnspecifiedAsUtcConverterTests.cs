using System.Text.Json;
using StockApp.Api.Json;
using Xunit;

namespace StockApp.Api.Tests.Json;

/// <summary>
/// Test unitario del converter, sin WebApplicationFactory ni Postgres: ejercita
/// JsonSerializer.Deserialize&lt;DateTime&gt; directo con las 4 formas de fecha que puede
/// mandar un cliente HTTP. Mismo espíritu que DomainExceptionHandlerTests: no hace falta
/// un host completo para probar una unidad aislada.
///
/// Bug real (verificación end-to-end BLOQUEANTE contra API + Postgres reales, ver
/// TareasEndpointTests para el circuito completo): un offset explícito no-UTC (ej.
/// "-03:00") hace que System.Text.Json deserialice a Kind=Local. El converter viejo
/// dejaba pasar Kind=Local sin tocar -- Npgsql rechaza escribir eso en columnas
/// timestamptz y tira 500. Los asserts comparan instante Y Kind a propósito: DateTime.Equals
/// (lo que usa Assert.Equal) NO compara Kind, así que un test que solo comparara el valor
/// podría pasar en una máquina con TZ local=UTC incluso con el bug presente.
/// </summary>
public class DateTimeUnspecifiedAsUtcConverterTests
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        Converters = { new DateTimeUnspecifiedAsUtcConverter() },
    };

    private static DateTime Leer(string json) =>
        JsonSerializer.Deserialize<DateTime>(json, Opciones);

    [Fact]
    public void Read_ConKindUtc_NoLoToca()
    {
        var resultado = Leer("\"2026-08-10T15:00:00Z\"");

        Assert.Equal(new DateTime(2026, 8, 10, 15, 0, 0, DateTimeKind.Utc), resultado);
        Assert.Equal(DateTimeKind.Utc, resultado.Kind);
    }

    [Fact]
    public void Read_ConKindUnspecified_LoInterpretaComoUtc()
    {
        // Comportamiento actual, preexistente: una fecha sin offset ni "Z" se asume UTC.
        // No lo rompe el fix del caso Local.
        var resultado = Leer("\"2026-08-10T15:00:00\"");

        Assert.Equal(new DateTime(2026, 8, 10, 15, 0, 0, DateTimeKind.Utc), resultado);
        Assert.Equal(DateTimeKind.Utc, resultado.Kind);
    }

    [Fact]
    public void Read_ConOffsetNegativoNoUtc_ConvierteAlInstanteUtcEquivalente()
    {
        // "-03:00" (zona de Argentina): 15:00 -03:00 == 18:00 UTC.
        var resultado = Leer("\"2026-08-10T15:00:00-03:00\"");

        Assert.Equal(new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc), resultado);
        Assert.Equal(DateTimeKind.Utc, resultado.Kind);
    }

    [Fact]
    public void Read_ConOffsetPositivoNoUtc_ConvierteAlInstanteUtcEquivalente()
    {
        // Offset distinto a propósito (+05:30, India): no depender de que la zona horaria
        // local de la máquina que corre el test coincida con la de Argentina para que el
        // caso anterior "funcione de casualidad".
        var resultado = Leer("\"2026-08-10T15:00:00+05:30\"");

        Assert.Equal(new DateTime(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc), resultado);
        Assert.Equal(DateTimeKind.Utc, resultado.Kind);
    }
}
