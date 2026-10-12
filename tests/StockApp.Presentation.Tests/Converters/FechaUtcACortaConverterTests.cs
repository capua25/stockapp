using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class FechaUtcACortaConverterTests
{
    [Fact]
    public void FechaUtc_SeFormateaConBarrasFijas_SinImportarLaCultura()
    {
        var antes = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var utc = new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc);
            var esperada = DateOnly.FromDateTime(utc.ToLocalTime()).ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture);
            Assert.Equal(esperada, FechaUtcACortaConverter.Instance.Convert(utc, typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Unspecified_SeTrataComoUtc()
    {
        var sinKind = new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Unspecified);
        var utc = DateTime.SpecifyKind(sinKind, DateTimeKind.Utc);
        Assert.Equal(FechaUtcACortaConverter.Instance.Convert(utc, typeof(string), null, CultureInfo.InvariantCulture),
            FechaUtcACortaConverter.Instance.Convert(sinKind, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NoEsFecha_EsUnGuion() =>
        Assert.Equal("—", FechaUtcACortaConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
