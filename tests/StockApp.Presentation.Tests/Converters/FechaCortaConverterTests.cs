using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class FechaCortaConverterTests
{
    [Fact]
    public void DateOnly_ConBarras_SinImportarLaCultura()
    {
        var antes = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // separador "."
        try
        {
            Assert.Equal("05/11/2026", FechaCortaConverter.Instance.Convert(new DateOnly(2026, 11, 5), typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Null_EsUnGuion() =>
        Assert.Equal("—", FechaCortaConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));

    [Fact]
    public void DateTime_FechaCalendario_NoSeConvierteALaZonaLocal()
    {
        // Un ingreso del 06/10 viaja como medianoche UTC: en Uruguay (UTC-3), pasarlo a hora local daría 05/10.
        var medianocheUtc = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        var casiMedianocheUtc = new DateTime(2026, 10, 6, 23, 59, 0, DateTimeKind.Utc);

        Assert.Equal("06/10/2026", FechaCortaConverter.Instance.Convert(medianocheUtc, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("06/10/2026", FechaCortaConverter.Instance.Convert(casiMedianocheUtc, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
