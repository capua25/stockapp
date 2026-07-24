using System;
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// F5d Entrega 2 Task 2: CalendarDatePicker/DatePicker de Avalonia bindean DateTimeOffset?, no
/// DateOnly? (tipo real de GastoAnalizadoDto.Fecha/FechaVencimiento) — este converter tapa esa
/// brecha. Sin componente de hora/zona en ningún lado: Convert usa 00:00 UTC-offset 0 fijo,
/// ConvertBack descarta hora/offset y se queda con la fecha calendario tal cual la eligió el
/// usuario (evita el bug clásico de "se guardó un día antes/después" por zona horaria).
/// </summary>
public class DateOnlyOffsetConverterTests
{
    private static readonly DateOnlyOffsetConverter Sut = DateOnlyOffsetConverter.Instance;

    [Fact]
    public void Convert_Null_DevuelveNull()
    {
        var resultado = Sut.Convert(null, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void Convert_DateOnly_DevuelveDateTimeOffsetConLaMismaFechaYOffsetCero()
    {
        var fecha = new DateOnly(2026, 3, 15);

        var resultado = Sut.Convert(fecha, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

        var dto = Assert.IsType<DateTimeOffset>(resultado);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(3, dto.Month);
        Assert.Equal(15, dto.Day);
        Assert.Equal(TimeSpan.Zero, dto.Offset);
    }

    [Fact]
    public void ConvertBack_Null_DevuelveNull()
    {
        var resultado = Sut.ConvertBack(null, typeof(DateOnly?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void ConvertBack_DateTimeOffset_DevuelveDateOnlyConLaMismaFechaCalendario()
    {
        var dto = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(-3));

        var resultado = Sut.ConvertBack(dto, typeof(DateOnly?), null, CultureInfo.InvariantCulture);

        Assert.Equal(new DateOnly(2026, 3, 15), resultado);
    }

    [Fact]
    public void RoundTrip_FechaFija_CierraAlConvertirYVolverAParsear()
    {
        var original = new DateOnly(2026, 12, 31);

        var offset = Sut.Convert(original, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);
        var vuelta = Sut.ConvertBack(offset, typeof(DateOnly?), null, CultureInfo.InvariantCulture);

        Assert.Equal(original, vuelta);
    }
}
