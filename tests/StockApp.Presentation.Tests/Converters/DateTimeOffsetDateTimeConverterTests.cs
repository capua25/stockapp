using System;
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class DateTimeOffsetDateTimeConverterTests
{
    private static readonly DateTimeOffsetDateTimeConverter Converter = new();

    // ── Convert (VM → control): DateTime? → DateTimeOffset? ─────────────────

    [Fact]
    public void Convert_ConValor_DevuelveDateTimeOffsetEquivalente()
    {
        var fecha = new DateTime(2026, 1, 15);

        var resultado = Converter.Convert(fecha, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

        Assert.Equal(new DateTimeOffset(fecha), resultado);
    }

    [Fact]
    public void Convert_ConNull_DevuelveNull()
    {
        var resultado = Converter.Convert(null, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    // ── ConvertBack (control → VM): DateTimeOffset? → DateTime? ─────────────

    [Fact]
    public void ConvertBack_ConValor_DevuelveDateTimeEquivalente()
    {
        var fecha = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        var resultado = Converter.ConvertBack(fecha, typeof(DateTime?), null, CultureInfo.InvariantCulture);

        Assert.Equal(fecha.DateTime, resultado);
    }

    [Fact]
    public void ConvertBack_ConNull_DevuelveNull()
    {
        var resultado = Converter.ConvertBack(null, typeof(DateTime?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }
}
