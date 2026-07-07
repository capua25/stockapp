using System;
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// Reproduce y verifica el fix del bug de huso horario: los movimientos se PERSISTEN en UTC
/// (correcto, ver MovimientoStockService/MovimientoStockRepository) pero se MOSTRABAN crudos,
/// sin convertir a hora local — un usuario en UTC-3 (Argentina) veía todo +3 horas adelantado.
/// </summary>
public class FechaUtcALocalConverterTests
{
    private static readonly FechaUtcALocalConverter Sut = FechaUtcALocalConverter.Instance;

    [Fact]
    public void Convert_FechaUtc_DevuelveHoraLocalEquivalente()
    {
        var utc = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Utc);
        var esperado = utc.ToLocalTime();

        var resultado = Sut.Convert(utc, typeof(DateTime), null, CultureInfo.InvariantCulture);

        Assert.Equal(esperado, resultado);
    }

    /// <summary>
    /// Gotcha real de EF Core + SQLite: al releer de la BD el DateTime vuelve con
    /// Kind=Unspecified aunque el valor almacenado sea un instante UTC. Sin forzar
    /// SpecifyKind(Utc) antes de convertir, el offset esperado no se aplica de forma
    /// confiable y el bug persiste. Este test usa el offset REAL del entorno
    /// (TimeZoneInfo.Local) en vez de hardcodear -3, para no acoplarse a la TZ de CI.
    /// </summary>
    [Fact]
    public void Convert_FechaConKindUnspecified_SeInterpretaComoUtcYConvierteAHoraLocal()
    {
        var comoVuelveDeSqlite = new DateTime(2026, 6, 10, 15, 0, 0, DateTimeKind.Unspecified);
        var comoUtc = DateTime.SpecifyKind(comoVuelveDeSqlite, DateTimeKind.Utc);
        var offsetLocal = TimeZoneInfo.Local.GetUtcOffset(comoUtc);
        var esperado = comoUtc + offsetLocal;

        var resultado = Sut.Convert(comoVuelveDeSqlite, typeof(DateTime), null, CultureInfo.InvariantCulture);

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void Convert_DateTimeNullableConValor_Convierte()
    {
        DateTime? valor = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var resultado = Sut.Convert(valor, typeof(DateTime?), null, CultureInfo.InvariantCulture);

        Assert.Equal(valor.Value.ToLocalTime(), resultado);
    }

    [Fact]
    public void Convert_Null_DevuelveNull()
    {
        var resultado = Sut.Convert(null, typeof(DateTime?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void Convert_ValorNoDateTime_DevuelveValorOriginalSinTocar()
    {
        var resultado = Sut.Convert("no es fecha", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("no es fecha", resultado);
    }

    [Fact]
    public void ConvertBack_Lanza_NotSupportedException()
    {
        Assert.Throws<NotSupportedException>(
            () => Sut.ConvertBack(DateTime.Now, typeof(DateTime), null, CultureInfo.InvariantCulture));
    }
}
