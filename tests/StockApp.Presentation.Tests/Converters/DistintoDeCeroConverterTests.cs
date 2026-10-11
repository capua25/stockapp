using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class DistintoDeCeroConverterTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(60, true)]
    [InlineData(-0.01, true)]
    public void Convierte(decimal valor, bool esperado) =>
        Assert.Equal(esperado, DistintoDeCeroConverter.Instance.Convert(valor, typeof(bool), null, CultureInfo.InvariantCulture));
}
