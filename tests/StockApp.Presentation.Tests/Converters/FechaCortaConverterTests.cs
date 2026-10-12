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
}
