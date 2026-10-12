using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class TamanoLegibleConverterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(850L, "850 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(182_400L, "178 KB")]
    [InlineData(870_400L, "850 KB")]
    [InlineData(1_048_064L, "1 MB")]
    [InlineData(1_258_291L, "1,2 MB")]
    [InlineData(10_485_760L, "10 MB")]
    public void Formatea_EnEsUy(long bytes, string esperado) =>
        Assert.Equal(esperado, TamanoLegibleConverter.Formatear(bytes));

    [Fact]
    public void ConLaCulturaAmbienteEnIngles_SigueConComa()
    {
        var antes = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            Assert.Equal("1,2 MB", TamanoLegibleConverter.Instance.Convert(1_258_291L, typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Null_DaVacio() =>
        Assert.Equal(string.Empty, TamanoLegibleConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
