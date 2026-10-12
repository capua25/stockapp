using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class PorcentajeConverterTests
{
    [Theory]
    [InlineData(47.3, "47,30 %")]
    [InlineData(94.26, "94,26 %")]
    [InlineData(100, "100,00 %")]
    [InlineData(114.3, "114,30 %")]
    [InlineData(0, "0,00 %")]
    public void Formatea_EnEsUy(decimal valor, string esperado) =>
        Assert.Equal(esperado, PorcentajeConverter.Instance.Convert(valor, typeof(string), null, new CultureInfo("en-US")));
}
